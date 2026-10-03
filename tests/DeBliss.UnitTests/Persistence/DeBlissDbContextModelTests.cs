using DeBliss.Domain.Billing;
using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DeBliss.UnitTests.Persistence;

public class DeBlissDbContextModelTests
{
    public static TheoryData<Type> CoreEntities => new()
    {
        typeof(User),
        typeof(Role),
        typeof(UserRole),
        typeof(Customer),
        typeof(CustomerNote),
        typeof(Staff),
        typeof(StaffService),
        typeof(StaffWorkingHour),
        typeof(StaffTimeOff),
        typeof(ServiceCategory),
        typeof(Service),
        typeof(Appointment),
        typeof(AppointmentService),
        typeof(AppointmentStatusHistory),
        typeof(BlockedTime),
        typeof(Invoice),
        typeof(InvoiceItem),
        typeof(Payment),
    };

    [Fact]
    public void The_model_maps_exactly_the_eighteen_core_entities()
    {
        Assert.Equal(18, TestModel.Model.GetEntityTypes().Count());
    }

    [Theory]
    [MemberData(nameof(CoreEntities))]
    public void Every_core_entity_is_mapped(Type entityType)
    {
        Assert.NotNull(TestModel.Model.FindEntityType(entityType));
    }

    [Theory]
    [MemberData(nameof(CoreEntities))]
    public void Every_table_uses_a_single_Guid_primary_key(Type entityType)
    {
        var primaryKey = TestModel.Entity(entityType).FindPrimaryKey();

        Assert.NotNull(primaryKey);

        var keyProperty = Assert.Single(primaryKey!.Properties);
        Assert.Equal(nameof(Entity.Id), keyProperty.Name);
        Assert.Equal(typeof(Guid), keyProperty.ClrType);
    }

    [Theory]
    [InlineData(typeof(User), "Users")]
    [InlineData(typeof(Role), "Roles")]
    [InlineData(typeof(UserRole), "UserRoles")]
    [InlineData(typeof(Customer), "Customers")]
    [InlineData(typeof(CustomerNote), "CustomerNotes")]
    [InlineData(typeof(Staff), "Staff")]
    [InlineData(typeof(StaffService), "StaffServices")]
    [InlineData(typeof(StaffWorkingHour), "StaffWorkingHours")]
    [InlineData(typeof(StaffTimeOff), "StaffTimeOff")]
    [InlineData(typeof(ServiceCategory), "ServiceCategories")]
    [InlineData(typeof(Service), "Services")]
    [InlineData(typeof(Appointment), "Appointments")]
    [InlineData(typeof(AppointmentService), "AppointmentServices")]
    [InlineData(typeof(AppointmentStatusHistory), "AppointmentStatusHistory")]
    [InlineData(typeof(BlockedTime), "BlockedTime")]
    [InlineData(typeof(Invoice), "Invoices")]
    [InlineData(typeof(InvoiceItem), "InvoiceItems")]
    [InlineData(typeof(Payment), "Payments")]
    public void Tables_are_named_explicitly(Type entityType, string tableName)
    {
        Assert.Equal(tableName, TestModel.Entity(entityType).GetTableName());
    }

    [Theory]
    [InlineData(typeof(Service), nameof(Service.Price))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.UnitPrice))]
    [InlineData(typeof(Invoice), nameof(Invoice.SubTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.DiscountTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.TaxTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.TotalAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.UnitPrice))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.DiscountAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.TaxAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.LineTotal))]
    [InlineData(typeof(Payment), nameof(Payment.Amount))]
    public void Money_is_stored_as_decimal_with_a_fixed_scale(Type entityType, string propertyName)
    {
        var property = TestModel.Entity(entityType).FindProperty(propertyName)!;

        Assert.Equal(typeof(decimal), property.ClrType);
        Assert.Equal(18, property.GetPrecision()!.Value);
        Assert.Equal(2, property.GetScale()!.Value);
    }

    [Fact]
    public void Invoice_quantity_allows_fractional_units()
    {
        var quantity = TestModel.Entity(typeof(InvoiceItem)).FindProperty(nameof(InvoiceItem.Quantity))!;

        Assert.Equal(typeof(decimal), quantity.ClrType);
        Assert.Equal(3, quantity.GetScale()!.Value);
    }

    [Theory]
    [InlineData(typeof(Appointment), nameof(Appointment.Status))]
    [InlineData(typeof(Appointment), nameof(Appointment.Source))]
    [InlineData(typeof(AppointmentStatusHistory), nameof(AppointmentStatusHistory.FromStatus))]
    [InlineData(typeof(AppointmentStatusHistory), nameof(AppointmentStatusHistory.ToStatus))]
    [InlineData(typeof(BlockedTime), nameof(BlockedTime.Type))]
    [InlineData(typeof(StaffTimeOff), nameof(StaffTimeOff.Type))]
    [InlineData(typeof(StaffWorkingHour), nameof(StaffWorkingHour.DayOfWeek))]
    [InlineData(typeof(Invoice), nameof(Invoice.Status))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.ItemType))]
    [InlineData(typeof(Payment), nameof(Payment.Method))]
    [InlineData(typeof(Payment), nameof(Payment.Status))]
    public void Enumerations_are_stored_as_readable_strings(Type entityType, string propertyName)
    {
        var property = TestModel.Entity(entityType).FindProperty(propertyName)!;

        Assert.Equal("nvarchar(20)", property.GetColumnType());
    }

    [Theory]
    [InlineData(typeof(Appointment), nameof(Appointment.StartsAt))]
    [InlineData(typeof(Appointment), nameof(Appointment.EndsAt))]
    [InlineData(typeof(AppointmentStatusHistory), nameof(AppointmentStatusHistory.ChangedAt))]
    [InlineData(typeof(Payment), nameof(Payment.PaidAt))]
    [InlineData(typeof(User), nameof(User.CreatedAt))]
    public void Timestamps_are_stored_as_datetimeoffset(Type entityType, string propertyName)
    {
        var property = TestModel.Entity(entityType).FindProperty(propertyName)!;

        Assert.Equal(typeof(DateTimeOffset), property.ClrType);
        Assert.Equal("datetimeoffset", property.GetColumnType());
    }

    [Fact]
    public void An_appointment_carries_many_service_lines()
    {
        var services = TestModel.Entity(typeof(Appointment)).FindNavigation(nameof(Appointment.Services))!;

        Assert.True(services.IsCollection);
        Assert.Equal(typeof(AppointmentService), services.TargetEntityType.ClrType);
    }

    [Fact]
    public void Each_service_line_can_name_its_own_staff_member()
    {
        var staff = TestModel.Entity(typeof(AppointmentService)).FindNavigation(nameof(AppointmentService.Staff))!;

        Assert.False(staff.IsCollection);
        Assert.Equal(typeof(Staff), staff.TargetEntityType.ClrType);
        Assert.True(TestModel.Entity(typeof(AppointmentService)).FindProperty(nameof(AppointmentService.StaffId))!.IsNullable);
    }

    [Fact]
    public void A_service_line_preserves_the_name_duration_and_price_booked_at_that_moment()
    {
        var appointmentService = TestModel.Entity(typeof(AppointmentService));

        Assert.NotNull(appointmentService.FindProperty(nameof(AppointmentService.ServiceNameSnapshot)));
        Assert.NotNull(appointmentService.FindProperty(nameof(AppointmentService.DurationMinutes)));
        Assert.NotNull(appointmentService.FindProperty(nameof(AppointmentService.UnitPrice)));
    }

    [Fact]
    public void An_invoice_line_preserves_the_price_actually_charged()
    {
        var invoiceItem = TestModel.Entity(typeof(InvoiceItem));

        Assert.NotNull(invoiceItem.FindProperty(nameof(InvoiceItem.UnitPrice)));
        Assert.NotNull(invoiceItem.FindProperty(nameof(InvoiceItem.LineTotal)));
    }

    [Fact]
    public void A_payment_records_its_method_amount_and_status()
    {
        var payment = TestModel.Entity(typeof(Payment));

        Assert.NotNull(payment.FindProperty(nameof(Payment.Method)));
        Assert.NotNull(payment.FindProperty(nameof(Payment.Amount)));
        Assert.NotNull(payment.FindProperty(nameof(Payment.Status)));
    }

    [Fact]
    public void A_service_is_offered_by_many_staff_and_a_staff_member_offers_many_services()
    {
        var staffServices = TestModel.Entity(typeof(StaffService));

        Assert.Equal(typeof(Staff), staffServices.FindNavigation(nameof(StaffService.Staff))!.TargetEntityType.ClrType);
        Assert.Equal(typeof(Service), staffServices.FindNavigation(nameof(StaffService.Service))!.TargetEntityType.ClrType);
    }

    [Fact]
    public void Working_hours_and_time_off_are_stored_in_separate_tables()
    {
        Assert.Equal("StaffWorkingHours", TestModel.Entity(typeof(StaffWorkingHour)).GetTableName());
        Assert.Equal("StaffTimeOff", TestModel.Entity(typeof(StaffTimeOff)).GetTableName());
        Assert.Equal("BlockedTime", TestModel.Entity(typeof(BlockedTime)).GetTableName());
    }

    [Fact]
    public void An_appointment_may_be_booked_without_a_customer_record_so_walk_ins_are_supported()
    {
        var appointment = TestModel.Entity(typeof(Appointment));

        Assert.True(appointment.FindProperty(nameof(Appointment.CustomerId))!.IsNullable);
        Assert.NotNull(appointment.FindProperty(nameof(Appointment.WalkInCustomerName)));
    }

    [Theory]
    [InlineData(typeof(Appointment))]
    [InlineData(typeof(Invoice))]
    [InlineData(typeof(Payment))]
    public void Records_that_must_never_be_physically_deleted_are_soft_deletable(Type entityType)
    {
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(entityType));
        Assert.NotNull(TestModel.Entity(entityType).FindProperty(nameof(ISoftDeletable.DeletedAt)));
    }

    [Theory]
    [InlineData(typeof(Appointment), nameof(Appointment.CustomerId))]
    [InlineData(typeof(Appointment), nameof(Appointment.RecurringSeriesId))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.ServiceId))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.StaffId))]
    [InlineData(typeof(Invoice), nameof(Invoice.AppointmentId))]
    [InlineData(typeof(Invoice), nameof(Invoice.CustomerId))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.AppointmentServiceId))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.ServiceId))]
    [InlineData(typeof(Payment), nameof(Payment.RefundOfPaymentId))]
    public void Deleting_a_referenced_record_never_removes_booking_or_financial_history(
        Type dependentType,
        string foreignKeyProperty)
    {
        Assert.Equal(DeleteBehavior.Restrict, DeleteBehaviorOf(dependentType, foreignKeyProperty));
    }

    [Theory]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.AppointmentId))]
    [InlineData(typeof(AppointmentStatusHistory), nameof(AppointmentStatusHistory.AppointmentId))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.InvoiceId))]
    [InlineData(typeof(Payment), nameof(Payment.InvoiceId))]
    [InlineData(typeof(CustomerNote), nameof(CustomerNote.CustomerId))]
    [InlineData(typeof(StaffWorkingHour), nameof(StaffWorkingHour.StaffId))]
    [InlineData(typeof(UserRole), nameof(UserRole.UserId))]
    public void Rows_owned_by_a_parent_are_removed_with_it(
        Type dependentType,
        string foreignKeyProperty)
    {
        Assert.Equal(DeleteBehavior.Cascade, DeleteBehaviorOf(dependentType, foreignKeyProperty));
    }

    [Fact]
    public void A_deleted_service_category_leaves_its_services_uncategorised_rather_than_deleting_them()
    {
        Assert.Equal(
            DeleteBehavior.SetNull,
            DeleteBehaviorOf(typeof(Service), nameof(Service.ServiceCategoryId)));
    }

    [Theory]
    [InlineData(typeof(Invoice), nameof(Invoice.InvoiceNumber))]
    [InlineData(typeof(User), nameof(User.Email))]
    [InlineData(typeof(Role), nameof(Role.Name))]
    public void Business_identifiers_are_unique(Type entityType, string propertyName)
    {
        var index = TestModel.Entity(entityType).GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual([propertyName]));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void A_staff_member_cannot_be_linked_to_the_same_service_twice()
    {
        var index = TestModel.Entity(typeof(StaffService)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(StaffService.StaffId), nameof(StaffService.ServiceId)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Appointments_are_indexed_for_calendar_range_queries()
    {
        var index = TestModel.Entity(typeof(Appointment)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(Appointment.StartsAt), nameof(Appointment.EndsAt)]));

        Assert.False(index.IsUnique);
    }

    private static DeleteBehavior DeleteBehaviorOf(Type dependentType, string foreignKeyProperty) =>
        TestModel.Entity(dependentType).GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([foreignKeyProperty]))
            .DeleteBehavior;
}
