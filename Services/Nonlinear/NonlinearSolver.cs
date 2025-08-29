using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Services.Nonlinear
{
    /// <summary>
    /// Simple 1D non-linear solver (projected gradient descent with Armijo backtracking).
    /// Minimizes user-provided expression f(x) on a box [L, U].
    ///
    /// Options (pass via 'options' dictionary):
    ///  - "Function"     : string, required. Example: "x^2 + 3*x + 2"
    ///  - "LowerBound"   : double, optional (default = -1e9)
    ///  - "UpperBound"   : double, optional (default = +1e9)
    ///  - "InitialX"     : double, optional (default = 0)
    ///  - "MaxIterations": int, optional (default = 2000)
    ///  - "Tolerance"    : double, optional (default = 1e-8)
    ///  - "Verbose"      : bool, optional (default = false)
    ///
    /// Returns SolutionResult with:
    ///  - ObjectiveValue = f(x*)
    ///  - VariableValues = [x*]
    ///  - Info["IterationLog"] = full textual log
    ///  - AlgorithmUsed = "Nonlinear 1D (Projected GD)"
    /// </summary>
    public sealed class NonlinearSolver : SolverBase
    {
        public override string AlgorithmName => "Nonlinear 1D (Projected GD)";
        public override string Description => "Minimize a 1D non-linear function f(x) with box bounds using projected gradient descent.";

        public override bool CanSolve(LPModel model) => true;

        public override Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "LowerBound", -1e9 },
            { "UpperBound", +1e9 },
            { "InitialX", 0.0 },
            { "MaxIterations", 2000 },
            { "Tolerance", 1e-8 },
            { "Verbose", false }
        };

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var started = DateTime.UtcNow;
            var opt = MergeOptions(options);

            // Read & validate options
            if (!opt.TryGetValue("Function", out var fObj) || fObj is not string fStr || string.IsNullOrWhiteSpace(fStr))
                return SolutionResult.CreateError(AlgorithmName, "Option 'Function' is required (e.g., Function = \"x^2 + 3*x\").");

            double L = GetOption(opt, "LowerBound", -1e9);
            double U = GetOption(opt, "UpperBound", +1e9);
            double x = GetOption(opt, "InitialX", 0.0);
            int maxIt = GetOption(opt, "MaxIterations", 2000);
            double tol = GetOption(opt, "Tolerance", 1e-8);
            bool verbose = GetOption(opt, "Verbose", false);

            if (double.IsNaN(L) || double.IsNaN(U) || L > U)
                return SolutionResult.CreateError(AlgorithmName, "Invalid bounds: LowerBound must be <= UpperBound.");

            // Ensure starting point is inside the box
            x = Project(x, L, U);

            // Build expression evaluator
            var eval = new ExpressionEvaluator(fStr);
            var log = new StringBuilder();
            log.AppendLine("=== NONLINEAR 1D (Projected Gradient Descent) ===");
            log.AppendLine($"f(x)   : {fStr}");
            log.AppendLine($"bounds : [{Round3(L)}, {Round3(U)}]");
            log.AppendLine($"x0     : {Round3(x)}");
            log.AppendLine();

            double fx = eval.Eval(x);
            double grad = NumericalGrad(eval, x);
            log.AppendLine($"Iter 0 : x = {Round3(x)}, f = {Round3(fx)}, |g| = {Round3(Math.Abs(grad))}");

            int iter = 0;
            const double boundTol = 1e-15; // for detecting exact-on-bound due to projection

            while (iter < maxIt)
            {
                iter++;

                // Stopping on gradient invalidity
                if (double.IsNaN(grad) || double.IsInfinity(grad))
                {
                    log.AppendLine("[STOP] Gradient invalid (NaN/Inf).");
                    break;
                }

                // ---- Projected optimality stopping rule (1D box) ----
                bool atLower = Math.Abs(x - L) <= boundTol;
                bool atUpper = Math.Abs(x - U) <= boundTol;
                double g = grad;

                if (!atLower && !atUpper)
                {
                    // Interior: classic gradient norm test
                    if (Math.Abs(g) < tol)
                    {
                        log.AppendLine($"[STOP] |grad| < tol ({tol}).");
                        break;
                    }
                }
                else
                {
                    // On a bound: only stop if there is NO feasible descent direction.
                    // Lower bound: feasible descent is + direction (need g >= -tol to stop)
                    // Upper bound: feasible descent is - direction (need g <= +tol to stop)
                    bool noFeasibleDescent =
                        (atLower && g >= -tol) ||
                        (atUpper && g <= +tol);

                    if (noFeasibleDescent)
                    {
                        // Tiny interior nudge to avoid flat-plateau false stops
                        double eps = Math.Max(1e-8, 1e-4 * Math.Max(1.0, Math.Abs(x)));
                        double xTry = atLower ? Math.Min(U, x + eps) : Math.Max(L, x - eps);
                        double fTry = eval.Eval(xTry);

                        if (fTry + 1e-12 >= fx)
                        {
                            log.AppendLine($"[STOP] Projected first-order condition satisfied at bound (tol={tol}).");
                            break;
                        }

                        // Accept nudge and continue
                        x = xTry;
                        fx = fTry;
                        grad = NumericalGrad(eval, x);
                        log.AppendLine($"  nudged from bound to x = {Round3(x)} (f={Round3(fx)})");
                        // (fall through to line search this iteration)
                    }
                }

                // Backtracking line search (Armijo)
                double step = 1.0;
                const double beta = 0.5;   // step shrink
                const double c1 = 1e-4;    // Armijo parameter

                double xTrial, fTrial;
                int btCount = 0;
                while (true)
                {
                    xTrial = Project(x - step * g, L, U);
                    fTrial = eval.Eval(xTrial);

                    // Armijo: f(x_s) <= f(x) - c1 * step * g^2  (since p = -g in 1D)
                    if (fTrial <= fx - c1 * step * g * g || step < 1e-16)
                        break;

                    step *= beta;
                    btCount++;
                }

                // Update
                double xPrev = x;
                x = xTrial;
                fx = fTrial;
                grad = NumericalGrad(eval, x);

                if (verbose)
                    log.AppendLine($"  backtracking steps = {btCount}");

                log.AppendLine($"Iter {iter} : x = {Round3(x)}, f = {Round3(fx)}, |g| = {Round3(Math.Abs(grad))}, step = {Round3(step)}");

                // Small parameter change stop
                if (Math.Abs(x - xPrev) < Math.Max(1e-15, tol * Math.Max(1.0, Math.Abs(x))))
                {
                    log.AppendLine($"[STOP] Small parameter change |Δx| below threshold ({tol}).");
                    break;
                }
            }

            var result = SolutionResult.CreateOptimal(
                objectiveValue: fx,
                variableValues: new[] { x },
                iterations: iter,
                algorithm: AlgorithmName,
                solveTimeMs: (DateTime.UtcNow - started).TotalMilliseconds,
                message: "Nonlinear 1D optimization finished."
            );

            // Preserve both keys to match your existing OutputWriter expectations
            result.Info["IterationLog"] = log.ToString();
            result.Info["Log"] = log.ToString();

            // Fill some "LP-like" extras to keep OutputWriter and UI happy
            result.ReducedCosts = null;
            result.DualValues = null;
            result.HasAlternateOptima = false;

            return result;
        }

        // ---------------- helpers ----------------

        private static double Project(double v, double L, double U)
            => v < L ? L : (v > U ? U : v);

        private static double NumericalGrad(ExpressionEvaluator f, double x)
        {
            // 5-point stencil for decent accuracy
            double h = 1e-6 * (1.0 + Math.Abs(x));
            double f1 = f.Eval(x - 2 * h);
            double f2 = f.Eval(x - h);
            double f3 = f.Eval(x + h);
            double f4 = f.Eval(x + 2 * h);
            return (f1 - 8 * f2 + 8 * f3 - f4) / (12 * h);
        }

        private static string Round3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        // ===== Very small, safe expression evaluator for f(x) =====
        private sealed class ExpressionEvaluator
        {
            private readonly string _s;
            private int _pos;

            public ExpressionEvaluator(string expr)
            {
                _s = expr ?? throw new ArgumentNullException(nameof(expr));
                _pos = 0;
            }

            public double Eval(double x)
            {
                _pos = 0;
                double val = ParseExpr(x);
                SkipWs();
                if (_pos != _s.Length)
                    throw new Exception($"Unexpected trailing characters at {_pos}.");
                return val;
            }

            // Grammar (classic):
            // Expr   := Term { ('+'|'-') Term }
            // Term   := Power { ('*'|'/') Power }
            // Power  := Factor { '^' Factor }
            // Factor := number | 'x' | '(' Expr ')' | ('+'|'-') Factor
            private double ParseExpr(double x)
            {
                double v = ParseTerm(x);
                while (true)
                {
                    SkipWs();
                    if (Match('+'))
                        v += ParseTerm(x);
                    else if (Match('-'))
                        v -= ParseTerm(x);
                    else
                        break;
                }
                return v;
            }
            private double ParseTerm(double x)
            {
                double v = ParsePower(x);
                while (true)
                {
                    SkipWs();
                    if (Match('*'))
                        v *= ParsePower(x);
                    else if (Match('/'))
                        v /= ParsePower(x);
                    else
                        break;
                }
                return v;
            }
            private double ParsePower(double x)
            {
                double v = ParseFactor(x);
                while (true)
                {
                    SkipWs();
                    if (Match('^'))
                    {
                        double e = ParseFactor(x);
                        v = Math.Pow(v, e);
                    }
                    else
                        break;
                }
                return v;
            }
            private double ParseFactor(double x)
            {
                SkipWs();
                if (Match('+'))
                    return ParseFactor(x);
                if (Match('-'))
                    return -ParseFactor(x);

                if (Match('('))
                {
                    double v = ParseExpr(x);
                    Require(')');
                    return v;
                }

                if (PeekIsX())
                {
                    _pos++; // consume 'x'
                    return x;
                }

                return ParseNumber();
            }

            private bool PeekIsX()
            {
                SkipWs();
                return _pos < _s.Length && (_s[_pos] == 'x' || _s[_pos] == 'X');
            }

            private double ParseNumber()
            {
                SkipWs();
                int start = _pos;
                bool hasDot = false;
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if ((c >= '0' && c <= '9') || c == '.')
                    {
                        if (c == '.')
                        {
                            if (hasDot)
                                break;
                            hasDot = true;
                        }
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
                if (start == _pos)
                    throw new Exception($"Number expected at position {start}.");

                var token = _s.Substring(start, _pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    throw new Exception($"Invalid number '{token}'.");

                return v;
            }

            private void SkipWs()
            {
                while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos]))
                    _pos++;
            }

            private bool Match(char c)
            {
                SkipWs();
                if (_pos < _s.Length && _s[_pos] == c)
                { _pos++; return true; }
                return false;
            }
            private void Require(char c)
            {
                SkipWs();
                if (_pos >= _s.Length || _s[_pos] != c)
                    throw new Exception($"Expected '{c}' at position {_pos}.");
                _pos++;
            }
        }
    }
}
