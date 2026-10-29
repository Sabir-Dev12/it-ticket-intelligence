using ITTicketIntelligence.Domain.Common;

namespace ITTicketIntelligence.Domain.ValueObjects;

/// <summary>A model score in [0, 1]; it does not imply a calibrated probability.</summary>
public readonly record struct PredictionConfidence
{
    public PredictionConfidence(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new DomainException("Prediction confidence must be a finite value between 0 and 1.");
        }

        Value = value;
    }

    public double Value { get; }
}
