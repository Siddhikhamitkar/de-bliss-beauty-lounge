using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers;
using DeBliss.Application.Customers.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The customer database the front desk searches all day: profiles, contact
/// details, notes and the read-only appointment history. Thin by design: every
/// rule lives in <see cref="ICustomerService"/>.
/// </summary>
[ApiController]
[Route("api/customers")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class CustomersController(
    ICustomerService customers,
    DeBliss.Application.Loyalty.ILoyaltyService loyalty) : ControllerBase
{
    [HttpGet("{id:guid}/loyalty")]
    public async Task<ActionResult<DeBliss.Application.Loyalty.Dtos.CustomerLoyaltySummaryDto>> GetLoyalty(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await loyalty.GetSummaryAsync(id, cancellationToken));

    [HttpGet("{id:guid}/loyalty/transactions")]
    public async Task<ActionResult<PagedResult<DeBliss.Application.Loyalty.Dtos.LoyaltyTransactionDto>>> ListLoyaltyTransactions(
        Guid id, CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(await loyalty.ListTransactionsAsync(id, page, pageSize, cancellationToken));

    /// <remarks>Will require <c>Permissions.Customers.ViewCustomers</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerSummaryDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] CustomerStatusFilter status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new CustomerQuery(search, status, page, pageSize);

        return Ok(await customers.SearchAsync(query, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Customers.CreateCustomer</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(
        [FromBody] SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var created = await customers.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.Customers.ViewCustomers</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await customers.GetAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Customers.EditCustomer</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Update(
        Guid id,
        [FromBody] SaveCustomerRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.UpdateAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Customers.ChangeCustomerStatus</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> SetStatus(
        Guid id,
        [FromBody] SetCustomerStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.SetStatusAsync(id, request.IsActive, cancellationToken));
    }

    /// <summary>
    /// Records the customer's WhatsApp consent, the switch that gates every
    /// appointment message the reminder engine sends. Reception captures it in
    /// person; nothing is ever sent to a customer whose record is not explicitly
    /// consented.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Customers.ManageWhatsAppOptIn</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}/whatsapp-opt-in")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> SetWhatsAppOptIn(
        Guid id,
        [FromBody] SetWhatsAppOptInRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.SetWhatsAppOptInAsync(id, request.Status, cancellationToken));
    }

    /// <summary>
    /// The appointment history behind the profile page. Reads the scheduling
    /// tables directly, so it is empty until the appointment module starts
    /// writing bookings and fills itself in from then on.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Customers.ViewCustomers</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}/appointments")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerAppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CustomerAppointmentDto>>> ListAppointments(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.ListAppointmentsAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Customers.ViewCustomers</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}/notes")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerNoteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CustomerNoteDto>>> ListNotes(
        Guid id,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.ListNotesAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Customers.ManageCustomerNotes</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/notes")]
    [ProducesResponseType(typeof(CustomerNoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerNoteDto>> AddNote(
        Guid id,
        [FromBody] SaveCustomerNoteRequest request,
        CancellationToken cancellationToken)
    {
        var created = await customers.AddNoteAsync(id, request, cancellationToken);

        return CreatedAtAction(nameof(ListNotes), new { id }, created);
    }

    /// <remarks>Will require <c>Permissions.Customers.ManageCustomerNotes</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}/notes/{noteId:guid}")]
    [ProducesResponseType(typeof(CustomerNoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerNoteDto>> UpdateNote(
        Guid id,
        Guid noteId,
        [FromBody] SaveCustomerNoteRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await customers.UpdateNoteAsync(id, noteId, request, cancellationToken));
    }
}
