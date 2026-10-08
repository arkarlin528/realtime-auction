using FluentValidation;

namespace Bidding.Application.Auctions;

public sealed class PlaceBidRequestValidator : AbstractValidator<PlaceBidRequest>
{
    public const decimal MaxBid = 10_000_000m;

    public PlaceBidRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaxBid)
            .PrecisionScale(12, 2, ignoreTrailingZeros: true)
            .WithMessage("Bids use at most 2 decimal places.");
    }
}

public sealed class CreateAuctionRequestValidator : AbstractValidator<CreateAuctionRequest>
{
    private static readonly string[] ContainerTypes = ["20GP", "40GP", "40HC", "20RF", "40RF", "45HC"];

    public CreateAuctionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.ContainerType).NotEmpty()
            .Must(t => ContainerTypes.Contains(t.ToUpperInvariant()))
            .WithMessage($"Container type must be one of {string.Join(", ", ContainerTypes)}.");
        RuleFor(x => x.Location).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Condition).NotEmpty().MaximumLength(100);
        RuleFor(x => x.YearBuilt).InclusiveBetween(1970, 2100);
        RuleFor(x => x.StartingPrice).GreaterThan(0).LessThanOrEqualTo(PlaceBidRequestValidator.MaxBid);
        RuleFor(x => x.MinIncrement).GreaterThan(0);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("The auction must end after it starts.");
        RuleFor(x => x).Must(x => x.EndsAt - x.StartsAt <= TimeSpan.FromDays(14))
            .WithName("EndsAt").WithMessage("Auctions can run for at most 14 days.");
    }
}
