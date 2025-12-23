using BenchmarkDotNet.Running;

namespace L2Cache.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("L2Cache Performance Benchmarks");
        Console.WriteLine("==============================");
        
        // Use BenchmarkSwitcher to allow running specific benchmarks or all of them
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
