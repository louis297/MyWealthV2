using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Domain.Events;

public class InstrumentDisabled : BaseEvent
{
    public InstrumentDisabled(Instrument instrument)
    {
        Instrument = instrument;
    }

    public Instrument Instrument { get; }
}
