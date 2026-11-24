using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Bank;
using MyWealthV2.Simulator.Broker;
using MyWealthV2.Simulator.Custodian;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Data;

/// <summary>
/// The simulator's own books on <c>MyWealthSimulatorDb</c> (simulator spec R14, §6.2). Mapping only: the
/// scripts in <c>database/simulator/</c> own the schema.
/// </summary>
public sealed class SimulatorDbContext(DbContextOptions<SimulatorDbContext> options) : DbContext(options)
{
    public DbSet<World> Worlds => Set<World>();

    public DbSet<ControlLogEntry> ControlLog => Set<ControlLogEntry>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<BankLine> BankLines => Set<BankLine>();

    public DbSet<BankFeedPlace> BankFeedPlaces => Set<BankFeedPlace>();

    public DbSet<BankPayment> BankPayments => Set<BankPayment>();

    public DbSet<MarketInstrument> MarketInstruments => Set<MarketInstrument>();

    public DbSet<MarketPrice> MarketPrices => Set<MarketPrice>();

    public DbSet<MarketCorporateAction> MarketCorporateActions => Set<MarketCorporateAction>();

    public DbSet<MarketCorporateActionVersion> MarketCorporateActionVersions => Set<MarketCorporateActionVersion>();

    public DbSet<BrokerSettings> BrokerSettings => Set<BrokerSettings>();

    public DbSet<BrokerOrder> BrokerOrders => Set<BrokerOrder>();

    public DbSet<BrokerFill> BrokerFills => Set<BrokerFill>();

    public DbSet<BrokerReport> BrokerReports => Set<BrokerReport>();

    public DbSet<BrokerSettlement> BrokerSettlements => Set<BrokerSettlement>();

    public DbSet<CustodianAccount> CustodianAccounts => Set<CustodianAccount>();

    public DbSet<CustodianMovement> CustodianMovements => Set<CustodianMovement>();

    public DbSet<CustodianTransfer> CustodianTransfers => Set<CustodianTransfer>();

    public DbSet<CustodianSettings> CustodianSettings => Set<CustodianSettings>();

    public DbSet<CustodianDividendPayment> CustodianDividendPayments => Set<CustodianDividendPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustodianAccount>(account =>
        {
            account.ToTable("CustodianAccounts");
            account.HasKey(row => row.Id);
            account.Property(row => row.AccountNumber).HasMaxLength(34);
            account.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            account.Property(row => row.Name).HasMaxLength(140);
        });

        modelBuilder.Entity<CustodianMovement>(movement =>
        {
            movement.ToTable("CustodianMovements");
            movement.HasKey(row => row.Id);
            movement.Ignore(row => row.Signed);
            movement.Property(row => row.AccountNumber).HasMaxLength(34);
            movement.Property(row => row.Symbol).HasMaxLength(32);
            movement.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            movement.Property(row => row.Quantity).HasColumnType("decimal(18,8)");
        });

        modelBuilder.Entity<CustodianTransfer>(transfer =>
        {
            transfer.ToTable("CustodianTransfers");
            transfer.HasKey(row => row.Id);
            transfer.Property(row => row.AccountNumber).HasMaxLength(34);
            transfer.Property(row => row.InstructionId).HasMaxLength(100);
            transfer.Property(row => row.RejectReason).HasMaxLength(40);
        });

        modelBuilder.Entity<CustodianDividendPayment>(payment =>
        {
            payment.ToTable("CustodianDividendPayments");
            payment.HasKey(row => row.Id);
            payment.Property(row => row.EntitledPosition).HasColumnType("decimal(18,8)");
            payment.Property(row => row.ElectedQuantity).HasColumnType("decimal(18,8)");
            payment.Property(row => row.Shares).HasColumnType("decimal(18,8)");
            payment.Property(row => row.Cash).HasColumnType("decimal(18,2)");
            payment.Property(row => row.ElectionInstructionId).HasMaxLength(100);
            payment.Property(row => row.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<CustodianSettings>(settings =>
        {
            settings.ToTable("CustodianSettings");
            settings.HasKey(row => row.Id);
            settings.Property(row => row.Id).ValueGeneratedNever();
            settings.Property(row => row.RejectReason).HasMaxLength(40);
            settings.Property(row => row.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<World>(world =>
        {
            world.ToTable("World");
            world.HasKey(row => row.Id);
            world.Property(row => row.Id).ValueGeneratedNever();
            world.Property(row => row.RowVersion).IsRowVersion();
            world.Ignore(row => row.Paused);
        });

        modelBuilder.Entity<ControlLogEntry>(entry =>
        {
            entry.ToTable("ControlLog");
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Actor).HasMaxLength(256);
            entry.Property(row => row.Action).HasMaxLength(50);
        });

        modelBuilder.Entity<BankAccount>(account =>
        {
            account.ToTable("BankAccounts");
            account.HasKey(row => row.Id);
            account.Property(row => row.AccountNumber).HasMaxLength(BankRules.AccountNumberMaxLength);
            account.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            account.Property(row => row.Name).HasMaxLength(BankRules.NameMaxLength);
        });

        modelBuilder.Entity<BankLine>(line =>
        {
            line.ToTable("BankLines");
            line.HasKey(row => row.Id);
            line.Property(row => row.OutsideId).HasMaxLength(100);
            line.Property(row => row.Amount).HasColumnType("decimal(18,4)");
            line.Property(row => row.PayerName).HasMaxLength(BankRules.FreeTextMaxLength);
            line.Property(row => row.PayerAccount).HasMaxLength(BankRules.AccountNumberMaxLength);
            line.Property(row => row.Reference).HasMaxLength(BankRules.FreeTextMaxLength);
            line.Property(row => row.EndToEndId).HasMaxLength(BankRules.InstructionIdMaxLength);
            line.Property(row => row.ReturnReason).HasMaxLength(BankRules.FreeTextMaxLength);
        });

        modelBuilder.Entity<BankPayment>(payment =>
        {
            payment.ToTable("BankPayments");
            payment.HasKey(row => row.Id);
            payment.Property(row => row.PaymentId).HasMaxLength(100);
            payment.Property(row => row.InstructionId).HasMaxLength(BankRules.InstructionIdMaxLength);
            payment.Property(row => row.RequestHash).HasMaxLength(32);
            payment.Property(row => row.Amount).HasColumnType("decimal(18,4)");
            payment.Property(row => row.Reference).HasMaxLength(BankRules.PaymentReferenceMaxLength);
            payment.Property(row => row.PayeeAccountNumber).HasMaxLength(BankRules.AccountNumberMaxLength);
            payment.Property(row => row.PayeeName).HasMaxLength(BankRules.NameMaxLength);
            payment.Property(row => row.RejectReason).HasMaxLength(30);
            payment.Property(row => row.ReturnOf).HasMaxLength(BankRules.InstructionIdMaxLength);
        });

        modelBuilder.Entity<MarketInstrument>(instrument =>
        {
            instrument.ToTable("MarketInstruments");
            instrument.HasKey(row => row.Id);
            instrument.Property(row => row.Symbol).HasMaxLength(MarketRules.SymbolMaxLength);
            instrument.Property(row => row.Name).HasMaxLength(MarketRules.NameMaxLength);
            instrument.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            instrument.Property(row => row.DailyMovePercent).HasColumnType("decimal(5,2)");
            instrument.Property(row => row.Price).HasColumnType("decimal(18,4)");
            instrument.Property(row => row.RowVersion).IsRowVersion();
            instrument.HasMany(row => row.Prices).WithOne().HasForeignKey(price => price.InstrumentId);
        });

        modelBuilder.Entity<MarketPrice>(price =>
        {
            price.ToTable("MarketPrices");
            price.HasKey(row => new { row.InstrumentId, row.PriceDate });
            price.Property(row => row.Price).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<MarketCorporateAction>(action =>
        {
            action.ToTable("MarketCorporateActions");
            action.HasKey(row => row.Id);
            action.Property(row => row.ExDate).HasColumnType("date");
            action.Property(row => row.RecordDate).HasColumnType("date");
            action.Property(row => row.PaymentDate).HasColumnType("date");
            action.Property(row => row.PaidOn).HasColumnType("date");
            action.Property(row => row.AmountPerShare).HasColumnType("decimal(18,8)");
            action.Property(row => row.ImputationCreditPerShare).HasColumnType("decimal(18,8)");
            action.Property(row => row.ForeignTaxRatePercent).HasColumnType("decimal(5,2)");
            action.Property(row => row.ReinvestmentPrice).HasColumnType("decimal(18,8)");
            action.Ignore(row => row.Terms);
            action.Property(row => row.RowVersion).IsRowVersion();
            action.HasMany(row => row.Versions).WithOne().HasForeignKey(version => version.ActionId);
        });

        modelBuilder.Entity<MarketCorporateActionVersion>(version =>
        {
            version.ToTable("MarketCorporateActionVersions");
            version.HasKey(row => row.Id);
            version.Property(row => row.ExDate).HasColumnType("date");
            version.Property(row => row.RecordDate).HasColumnType("date");
            version.Property(row => row.PaymentDate).HasColumnType("date");
            version.Property(row => row.AmountPerShare).HasColumnType("decimal(18,8)");
            version.Property(row => row.ImputationCreditPerShare).HasColumnType("decimal(18,8)");
            version.Property(row => row.ForeignTaxRatePercent).HasColumnType("decimal(5,2)");
            version.Property(row => row.ReinvestmentPrice).HasColumnType("decimal(18,8)");
        });

        modelBuilder.Entity<BrokerSettings>(settings =>
        {
            settings.ToTable("BrokerSettings");
            settings.HasKey(row => row.Id);
            settings.Property(row => row.Id).ValueGeneratedNever();
            settings.Property(row => row.SlippagePercent).HasColumnType("decimal(5,2)");
            settings.Property(row => row.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<BrokerOrder>(order =>
        {
            order.ToTable("BrokerOrders");
            order.HasKey(row => row.Id);
            order.Property(row => row.ClientOrderId).HasMaxLength(BrokerRules.ClientOrderIdMaxLength);
            order.Property(row => row.BodyHash).HasMaxLength(32);
            order.Property(row => row.Symbol).HasMaxLength(BrokerRules.SymbolMaxLength);
            order.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            order.Property(row => row.Quantity).HasColumnType("decimal(18,8)");
            order.Property(row => row.SafekeepingAccount).HasMaxLength(34);
            order.Property(row => row.PriceLimit).HasColumnType("decimal(18,8)");
            order.Property(row => row.ReferencePrice).HasColumnType("decimal(18,8)");
            order.Property(row => row.FilledQuantity).HasColumnType("decimal(18,8)");
            order.Property(row => row.AcceptedDay).HasColumnType("date");
            order.Property(row => row.SettlementAccount).HasMaxLength(34);
            order.Property(row => row.RowVersion).IsRowVersion();
            order.Ignore(row => row.Rest);
            order.HasMany(row => row.Fills).WithOne().HasForeignKey(fill => fill.OrderId);
        });

        modelBuilder.Entity<BrokerFill>(fill =>
        {
            fill.ToTable("BrokerFills");
            fill.HasKey(row => row.Id);
            fill.Property(row => row.Quantity).HasColumnType("decimal(18,8)");
            fill.Property(row => row.Price).HasColumnType("decimal(18,4)");
            fill.Property(row => row.TradeDate).HasColumnType("date");
            fill.Property(row => row.SettlementDate).HasColumnType("date");
            fill.Property(row => row.ContractNote).HasMaxLength(50);
            fill.HasOne<BrokerSettlement>().WithMany().HasForeignKey(row => row.SettlementId);
        });

        modelBuilder.Entity<BrokerSettlement>(settlement =>
        {
            settlement.ToTable("BrokerSettlements");
            settlement.HasKey(row => row.Id);
            settlement.Property(row => row.SettlementAccount).HasMaxLength(34);
            settlement.Property(row => row.Currency).HasColumnType("char(3)").IsFixedLength();
            settlement.Property(row => row.SettlementDate).HasColumnType("date");
            settlement.Property(row => row.NetAmount).HasColumnType("decimal(18,4)");
            settlement.Property(row => row.PaidAmount).HasColumnType("decimal(18,4)");
        });

        modelBuilder.Entity<BrokerReport>(report =>
        {
            report.ToTable("BrokerReports");
            report.HasKey(row => row.Id);
            report.Property(row => row.SettledDate).HasColumnType("date");
            report.Property(row => row.Reason).HasMaxLength(140);
            report.HasOne(row => row.Order).WithMany().HasForeignKey(row => row.OrderId).IsRequired(false);
            report.HasOne(row => row.Fill).WithMany().HasForeignKey(row => row.FillId);
            report.HasOne(row => row.Settlement).WithMany().HasForeignKey(row => row.SettlementId);
        });

        modelBuilder.Entity<BankFeedPlace>(place =>
        {
            place.ToTable("BankFeedPlaces");
            place.HasKey(row => row.Place);
        });
    }
}
