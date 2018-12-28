namespace ConsoleHost.Events
{
    internal class ValidityPeriodResetEvent
    {
        public string DocumentNumber { get; set; }
        public int Sequence { get; set; }
    }
}