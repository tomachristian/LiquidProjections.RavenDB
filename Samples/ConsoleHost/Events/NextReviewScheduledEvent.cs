using System;

namespace ConsoleHost.Events
{
    internal class NextReviewScheduledEvent
    {
        public string DocumentNumber { get; set; }
        public DateTime NextReviewAt { get; set; }
    }
}