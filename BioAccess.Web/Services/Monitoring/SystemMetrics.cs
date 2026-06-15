using System.Threading;

namespace BioAccess.Web.Services.Monitoring;

public class SystemMetrics
{
    private int _assignSuccess;
    private int _assignFail;
    private int _unassignSuccess;
    private int _unassignFail;
    private int _timeoutCount;

    public int AssignSuccess => _assignSuccess;
    public int AssignFail => _assignFail;
    public int UnassignSuccess => _unassignSuccess;
    public int UnassignFail => _unassignFail;
    public int TimeoutCount => _timeoutCount;

    public void IncrementAssignSuccess() => Interlocked.Increment(ref _assignSuccess);
    public void IncrementAssignFail() => Interlocked.Increment(ref _assignFail);
    public void IncrementUnassignSuccess() => Interlocked.Increment(ref _unassignSuccess);
    public void IncrementUnassignFail() => Interlocked.Increment(ref _unassignFail);
    public void IncrementTimeout() => Interlocked.Increment(ref _timeoutCount);
}
