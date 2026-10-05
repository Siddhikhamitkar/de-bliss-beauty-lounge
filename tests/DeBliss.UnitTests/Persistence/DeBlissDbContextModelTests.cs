using DeBliss.Domain.Billing;
using DeBliss.Domain.Common;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Notifications;
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
        typeof(ServiceBundle),
        typeof(ServiceBundleItem),
        typeof(Appointment),
        typeof(AppointmentService),
        typeof(AppointmentStatusHistory),
        typeof(BlockedTime),
        typeof(Invoice),
        typeof(InvoiceItem),
        typeof(Payment),
        typeof(Coupon),
        typeof(ReminderRule),
        typeof(AppointmentReminder),
    };

    [Fact]
    public void The_model_maps_exactly_the_twenty_three_core_entities()
    {
        Assert.Equal(23, TestModel.Model.GetEntityTypes().Count());
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
    [InlineData(typeof(ServiceBundle), "ServiceBundles")]
    [InlineData(typeof(ServiceBundleItem), "ServiceBundleItems")]
    [InlineData(typeof(Appointment), "Appointments")]
    [InlineData(typeof(AppointmentService), "AppointmentServices")]
    [InlineData(typeof(AppointmentStatusHistory), "AppointmentStatusHistory")]
    [InlineData(typeof(BlockedTime), "BlockedTime")]
    [InlineData(typeof(Invoice), "Invoices")]
    [InlineData(typeof(InvoiceItem), "InvoiceItems")]
    [InlineData(typeof(Payment), "Payments")]
    [InlineData(typeof(Coupon), "Coupons")]
    [InlineData(typeof(ReminderRule), "ReminderRules")]
    [InlineData(typeof(AppointmentReminder), "AppointmentReminders")]
    public void Tables_are_named_explicitly(Type entityType, string tableName)
    {
        Assert.Equal(tableName, TestModel.Entity(entityType).GetTableName());
    }

    [Theory]
    [InlineData(typeof(Service), nameof(Service.Price))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.UnitPrice))]
    [InlineData(typeof(Invoice), nameof(Invoice.SubTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.DiscountTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.CouponDiscount))]
    [InlineData(typeof(Invoice), nameof(Invoice.ManualDiscount))]
    [InlineData(typeof(Invoice), nameof(Invoice.BundleDiscount))]
    [InlineData(typeof(Invoice), nameof(Invoice.TaxTotal))]
    [InlineData(typeof(Invoice), nameof(Invoice.TotalAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.UnitPrice))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.DiscountAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.TaxAmount))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.LineTotal))]
    [InlineData(typeof(Payment), nameof(Payment.Amount))]
    [InlineData(typeof(Coupon), nameof(Coupon.DiscountValue))]
    public void Money_is_stored_as_decimal_with_a_fixed_scale(Type entityType, string propertyName)
    {
        var property = TestModel.Entity(entityType).FindProperty(propertyName)!;

        Assert.Equal(typeof(decimal), property.ClrType);
        Assert.Equal(18, property.GetPrecision()!.Value);
        Assert.Equal(2, property.GetScale()!.Value);
    }

    [Fact]
    public void A_bundle_price_snapshot_on_an_appointment_is_optional_but_keeps_fixed_scale()
    {
        var property = TestModel.Entity(typeof(Appointment)).FindProperty(nameof(Appointment.BundlePriceSnapshot))!;

        Assert.Equal(typeof(decimal?), property.ClrType);
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
    [InlineData(typeof(Coupon), nameof(Coupon.DiscountType))]
    [InlineData(typeof(ReminderRule), nameof(ReminderRule.ReminderType))]
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.ReminderType))]
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.Status))]
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.Channel))]
    [InlineData(typeof(Customer), nameof(Customer.WhatsAppOptInStatus))]
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
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.ScheduledAt))]
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
    [InlineData(typeof(Appointment), nameof(Appointment.ServiceBundleId))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.ServiceId))]
    [InlineData(typeof(AppointmentService), nameof(AppointmentService.StaffId))]
    [InlineData(typeof(Invoice), nameof(Invoice.AppointmentId))]
    [InlineData(typeof(Invoice), nameof(Invoice.CustomerId))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.AppointmentServiceId))]
    [InlineData(typeof(InvoiceItem), nameof(InvoiceItem.ServiceId))]
    [InlineData(typeof(Invoice), nameof(Invoice.CouponId))]
    [InlineData(typeof(Invoice), nameof(Invoice.ServiceBundleId))]
    [InlineData(typeof(ServiceBundleItem), nameof(ServiceBundleItem.ServiceId))]
    [InlineData(typeof(Payment), nameof(Payment.RefundOfPaymentId))]
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.CustomerId))]
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
    [InlineData(typeof(ServiceBundleItem), nameof(ServiceBundleItem.ServiceBundleId))]
    [InlineData(typeof(UserRole), nameof(UserRole.UserId))]
    [InlineData(typeof(AppointmentReminder), nameof(AppointmentReminder.AppointmentId))]
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

        Assert.Equal(
            DeleteBehavior.SetNull,
            DeleteBehaviorOf(typeof(ServiceBundle), nameof(ServiceBundle.ServiceCategoryId)));
    }

    [Fact]
    public void A_bundle_cannot_list_the_same_service_twice()
    {
        var index = TestModel.Entity(typeof(ServiceBundleItem)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ServiceBundleItem.ServiceBundleId), nameof(ServiceBundleItem.ServiceId)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void A_bundle_knows_its_components_and_a_component_knows_its_bundles()
    {
        var bundle = TestModel.Entity(typeof(ServiceBundle));

        Assert.Equal(typeof(ServiceBundleItem), bundle.FindNavigation(nameof(ServiceBundle.Items))!.TargetEntityType.ClrType);
        Assert.Equal(typeof(ServiceCategory), bundle.FindNavigation(nameof(ServiceBundle.ServiceCategory))!.TargetEntityType.ClrType);

        var item = TestModel.Entity(typeof(ServiceBundleItem));

        Assert.Equal(typeof(ServiceBundle), item.FindNavigation(nameof(ServiceBundleItem.ServiceBundle))!.TargetEntityType.ClrType);
        Assert.Equal(typeof(Service), item.FindNavigation(nameof(ServiceBundleItem.Service))!.TargetEntityType.ClrType);
    }

    [Theory]
    [InlineData(typeof(Invoice), nameof(Invoice.InvoiceNumber))]
    [InlineData(typeof(User), nameof(User.Email))]
    [InlineData(typeof(Role), nameof(Role.Name))]
    [InlineData(typeof(Coupon), nameof(Coupon.Code))]
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

    [Fact]
    public void A_pending_reminder_for_the_same_appointment_type_and_moment_cannot_be_planned_twice()
    {
        var index = TestModel.Entity(typeof(AppointmentReminder)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([
                nameof(AppointmentReminder.AppointmentId),
                nameof(AppointmentReminder.ReminderType),
                nameof(AppointmentReminder.ScheduledAt)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[Status] = 'Pending'", index.GetFilter());
    }

    [Fact]
    public void A_sent_message_id_can_belong_to_only_one_reminder()
    {
        var index = TestModel.Entity(typeof(AppointmentReminder)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(AppointmentReminder.ProviderMessageId)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[ProviderMessageId] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Reminders_are_indexed_for_the_due_scan()
    {
        var index = TestModel.Entity(typeof(AppointmentReminder)).GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([
                nameof(AppointmentReminder.Status),
                nameof(AppointmentReminder.ScheduledAt)]));

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Reminder_rules_ship_with_the_default_timing_seeded()
    {
        var rules = TestModel.DesignTimeEntity(typeof(ReminderRule)).GetSeedData()
            .Select(rule => (
                Type: rule[nameof(ReminderRule.ReminderType)]?.ToString(),
                OffsetMinutes: (int)rule[nameof(ReminderRule.OffsetMinutes)]!,
                IsEnabled: (bool)rule[nameof(ReminderRule.IsEnabled)]!))
            .ToArray();

        Assert.Equal(5, rules.Length);

        Assert.Contains(rules, rule => rule is { Type: "Confirmation", OffsetMinutes: 0, IsEnabled: true });
        Assert.Contains(rules, rule => rule is { Type: "Reminder", OffsetMinutes: 24 * 60, IsEnabled: true });
        Assert.Contains(rules, rule => rule is { Type: "Reminder", OffsetMinutes: 2 * 60, IsEnabled: true });
        Assert.Contains(rules, rule => rule is { Type: "Cancellation", OffsetMinutes: 0, IsEnabled: true });
        Assert.Contains(rules, rule => rule is { Type: "Rescheduled", OffsetMinutes: 0, IsEnabled: true });
    }

    [Fact]
    public void A_customer_starts_with_unknown_whatsapp_opt_in_rather_than_implied_consent()
    {
        var property = TestModel.Entity(typeof(Customer)).FindProperty(nameof(Customer.WhatsAppOptInStatus))!;

        Assert.False(property.IsNullable);
        Assert.Equal("nvarchar(20)", property.GetColumnType());
        Assert.Equal("Unknown", property.GetDefaultValue()?.ToString());
    }

    private static DeleteBehavior DeleteBehaviorOf(Type dependentType, string foreignKeyProperty) =>
        TestModel.Entity(dependentType).GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([foreignKeyProperty]))
            .DeleteBehavior;
}
