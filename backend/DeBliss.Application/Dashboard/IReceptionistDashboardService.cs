using DeBliss.Application.Dashboard.Dtos;

namespace DeBliss.Application.Dashboard;

/// <summary>
/// Composes the receptionist's read-only snapshot of today from the scheduling,
/// staffing and customer data the other modules already own. Nothing here
/// mutates; the dashboard is a landing view onto existing workflows.
/// </summary>
public interface IReceptionistDashboardService
{
    Task<ReceptionistDashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
