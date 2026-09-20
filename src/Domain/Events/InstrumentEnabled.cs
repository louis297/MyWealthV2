using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class InstrumentEnabled : BaseEvent
{
    public InstrumentEnabled(Instrument instrument)
    {
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
}
