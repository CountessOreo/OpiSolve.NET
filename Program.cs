using OptiSolver.NET.UI;

namespace OptiSolver.NET
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length >= 1)
            {
                var path = args[0];
                var solver = args.Length >= 2 ? args[1] : "revised";

                var ctrl = new Controller.SolverController();
                var res = ctrl.SolveFromFile(path, solver);
                IO.OutputWriter.WriteToConsole(res);
                return;
            }

            Menu.Run();
        }
    }
}
