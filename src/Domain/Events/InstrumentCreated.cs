using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class InstrumentCreated : BaseEvent
{
    public InstrumentCreated(Instrument instrument)
    {
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
}
