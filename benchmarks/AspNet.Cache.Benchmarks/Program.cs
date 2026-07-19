using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(AspNet.Cache.Benchmarks.CacheBenchmarks).Assembly).Run(args);
