using Concurrency.Shared;

await ConsoleLab.Run("Chapter 19 — Appendix",
[
    ("Show appendix index", () =>
    {
        Console.WriteLine("  See per-folder README.md files:");
        Console.WriteLine("    19-Appendix/InterviewQuestions");
        Console.WriteLine("    19-Appendix/CheatSheets");
        Console.WriteLine("    19-Appendix/FurtherReading");
        Console.WriteLine("    19-Appendix/AcademicPapers");
        Console.WriteLine("    19-Appendix/Glossary");
        return Task.CompletedTask;
    })
],
args);
