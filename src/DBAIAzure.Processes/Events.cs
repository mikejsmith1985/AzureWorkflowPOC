namespace DBAIAzure.Processes;

public static class Events
{
    // Entry
    public const string TicketReceived = "TicketReceived";

    // IntakeStep output
    public const string IntakeComplete = "IntakeComplete";

    // ValidationStep inputs + outputs
    public const string Validate = "Validate";
    public const string ReadyPath = "ReadyPath";
    public const string NotReadyPath = "NotReadyPath";
    public const string Blocked = "Blocked";

    // GapAnalysisStep output
    public const string QuestionsReady = "QuestionsReady";

    // HitlPauseStep inputs + output
    public const string AwaitHuman = "AwaitHuman";
    public const string HumanResponded = "HumanResponded";

    // EstimationStep inputs + output
    public const string Estimate = "Estimate";
    public const string EstimationComplete = "EstimationComplete";

    // ActionStep input
    public const string CreateJira = "CreateJira";
}
