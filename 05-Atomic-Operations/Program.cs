using Chapter05.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 05 — Atomic Operations",
[
    ("Interlocked.Increment vs lock",         CounterContention.Run),
    ("CAS-based lock-free update",            CasUpdateDemo.Run),
    ("Treiber stack — lock-free linked list", TreiberStackDemo.Run),
    ("ABA simulation",                         AbaDemo.Run),
],
args);
