using Chapter00.Demos;
using Concurrency.Shared;

await ConsoleLab.Run("Chapter 00 — Prerequisites: how the hardware behaves",
[
    ("Cache line size probe",         CacheLineProbe.Run),
    ("False sharing — with/without padding", FalseSharingDemo.Run),
    ("Context switch cost (ping-pong)",      ContextSwitchDemo.Run),
    ("Allocation locality observation",      LocalityDemo.Run),
],
args);
