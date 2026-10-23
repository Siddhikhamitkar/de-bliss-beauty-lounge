using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public sealed class SalarySlipConfiguration : IEntityTypeConfiguration<SalarySlip>
{
    public void Configure(EntityTypeBuilder<SalarySlip> builder)
    {
        builder.ToTable("SalarySlips");
        builder.HasKey(slip => slip.Id);
        builder.Property(slip => slip.Id).ValueGeneratedNever();
        builder.Property(slip => slip.EmployeeName).HasMaxLength(201).IsRequired();
        builder.Property(slip => slip.EmployeeId).HasMaxLength(100);
        builder.Property(slip => slip.PayDate).HasColumnType("date");
        builder.Property(slip => slip.WorkingDays).HasPrecision(4, 1);
        builder.Property(slip => slip.TotalLeave).HasPrecision(4, 1);
        builder.Property(slip => slip.LeaveWithoutPay).HasPrecision(4, 1);
        builder.Property(slip => slip.AgreedTotalSalary).HasPrecision(18, 2);
        builder.Property(slip => slip.BasicSalary).HasPrecision(18, 2);
        builder.Property(slip => slip.HouseRentAllowance).HasPrecision(18, 2);
        builder.Property(slip => slip.OtherAllowance).HasPrecision(18, 2);
        builder.Property(slip => slip.Commission).HasPrecision(18, 2);
        builder.Property(slip => slip.LeaveDeduction).HasPrecision(18, 2);
        builder.Property(slip => slip.OtherDeductions).HasPrecision(18, 2);
        builder.Property(slip => slip.TotalEarnings).HasPrecision(18, 2);
        builder.Property(slip => slip.TotalDeductions).HasPrecision(18, 2);
        builder.Property(slip => slip.NetSalary).HasPrecision(18, 2);
        builder.Property(slip => slip.BankName).HasMaxLength(200);
        builder.Property(slip => slip.AccountName).HasMaxLength(201).IsRequired();
        builder.Property(slip => slip.BankAccountNumber).HasMaxLength(100);
        builder.Property(slip => slip.IbanNumber).HasMaxLength(50);
        builder.Property(slip => slip.Pdf).HasColumnType("varbinary(max)").IsRequired();
        builder.Property(slip => slip.TemplateVersion).HasMaxLength(20).HasDefaultValue("1").IsRequired();

        builder.HasIndex(slip => new { slip.StaffId, slip.SalaryYear, slip.SalaryMonth }).IsUnique();
        builder.HasOne(slip => slip.Staff)
            .WithMany()
            .HasForeignKey(slip => slip.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
