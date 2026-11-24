using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Infrastructure.Data.Configurations;

/// <summary>Corporate actions §6.1: a Tenant's corporate actions (script 0050); the dividend terms (dividends §6.1,
/// script 0057).</summary>
public class CorporateActionConfiguration : IEntityTypeConfiguration<CorporateAction>
{
    public void Configure(EntityTypeBuilder<CorporateAction> builder)
    {
        builder.ToTable("CorporateActions");
        builder.Ignore(action => action.Ratio);
        builder.Ignore(action => action.Terms);
        builder.Property(action => action.AmountPerShare).HasColumnType("decimal(18,8)");
        builder.Property(action => action.ImputationCreditPerShare).HasColumnType("decimal(18,8)");
        builder.Property(action => action.ForeignTaxRatePercent).HasColumnType("decimal(5,2)");
        builder.Property(action => action.ReinvestmentPrice).HasColumnType("decimal(18,8)");
        builder.Property(action => action.ReceivedAmount).HasColumnType("decimal(18,4)");
        builder.Property(action => action.PublicId).IsRequired();
        builder.Property(action => action.ExDate).HasColumnType("date").IsRequired();
        builder.Property(action => action.RecordDate).HasColumnType("date");
        builder.Property(action => action.PaymentDate).HasColumnType("date");
        builder.Property(action => action.Connection).HasMaxLength(CorporateAction.ConnectionMaxLength);
        builder.Property(action => action.PartyActionId).HasMaxLength(CorporateAction.PartyActionIdMaxLength);
        builder.Property(action => action.Memo).HasMaxLength(CorporateAction.MemoMaxLength);
        builder.Property(action => action.CancelNote).HasMaxLength(CorporateAction.NoteMaxLength);
        builder.Property(action => action.CreatedBy).HasMaxLength(450).IsRequired();
        builder.Property(action => action.RowVersion).IsRowVersion();
        builder.HasIndex(action => action.PublicId).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(action => action.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>()
            .WithMany()
            .HasForeignKey(action => action.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(action => action.EffectiveByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(action => action.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Corporate actions §6.1: one entitlement per corporate action and Custodial account (script 0050), with the
/// dividend fields (dividends §6.1, script 0057).</summary>
public class CorporateActionEntitlementConfiguration : IEntityTypeConfiguration<CorporateActionEntitlement>
{
    public void Configure(EntityTypeBuilder<CorporateActionEntitlement> builder)
    {
        builder.ToTable("CorporateActionEntitlements");
        builder.HasKey(entitlement => entitlement.Id);
        builder.Property(entitlement => entitlement.Id).ValueGeneratedOnAdd();
        builder.Property(entitlement => entitlement.QuantityBefore).HasColumnType("decimal(18,8)");
        builder.Property(entitlement => entitlement.QuantityAfter).HasColumnType("decimal(18,8)");
        builder.Ignore(entitlement => entitlement.IsDividend);
        builder.Property(entitlement => entitlement.EntitledQuantity).HasColumnType("decimal(18,8)");
        builder.Property(entitlement => entitlement.GrossAmount).HasColumnType("decimal(18,4)");
        builder.Property(entitlement => entitlement.ImputationCredit).HasColumnType("decimal(18,4)");
        builder.Property(entitlement => entitlement.ForeignTax).HasColumnType("decimal(18,4)");
        builder.Property(entitlement => entitlement.ParticipatingQuantity).HasColumnType("decimal(18,8)");
        builder.Property(entitlement => entitlement.ReinvestedQuantity).HasColumnType("decimal(18,8)");
        builder.Property(entitlement => entitlement.NetAmount).HasColumnType("decimal(18,4)");
        builder.Property(entitlement => entitlement.NetCurrency).HasColumnType("char(3)").IsFixedLength();
        builder.Property(entitlement => entitlement.RowVersion).IsRowVersion();
        builder.HasIndex(entitlement => new { entitlement.CorporateActionId, entitlement.AccountId }).IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CorporateAction>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.CorporateActionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(entitlement => entitlement.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
