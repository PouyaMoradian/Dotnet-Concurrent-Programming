using Chapter03.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 03 — The ThreadPool",
[
    ("Show pool sizing",            PoolSizingDemo.Run),
    ("Hill-climb observation",      HillClimbDemo.Run),
    ("Starvation reproduction",     StarvationDemo.Run),
    ("LongRunning vs Task.Run",     LongRunningDemo.Run),
    ("Custom scheduler — sequential", CustomSchedulerDemo.Run),
],
args);
