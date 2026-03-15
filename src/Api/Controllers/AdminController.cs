using Application;
using Application.DTOs.Admin;
using Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using Quartz.Impl.Matchers;
using System.Security.Claims;

namespace Api.Controllers;

[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly IAuditLogService _auditLogService;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobHistoryCache _jobHistory;

    public AdminController(
        IAdminService adminService,
        IAuditLogService auditLogService,
        ISchedulerFactory schedulerFactory,
        IJobHistoryCache jobHistory)
    {
        _adminService = adminService;
        _auditLogService = auditLogService;
        _schedulerFactory = schedulerFactory;
        _jobHistory = jobHistory;
    }

    /// <summary>
    /// Returns a paginated, searchable list of all users.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("users")]
    public async Task<IActionResult> GetUsersAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var result = await _adminService.GetUsersAsync(page, pageSize, search);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return Ok(result.Data);
    }

    /// <summary>
    /// Returns full details for a single user, including active sessions.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("users/{userId}")]
    public async Task<IActionResult> GetUserByIdAsync(string userId)
    {
        var result = await _adminService.GetUserByIdAsync(userId);
        if (result.IsFailure) return NotFound(new { result.Error });
        return Ok(result.Value);
    }

    /// <summary>
    /// Assigns a role to a user. Idempotent.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("users/{userId}/roles")]
    public async Task<IActionResult> AssignRoleAsync(string userId, [FromBody] AdminAssignRoleDto dto)
    {
        var result = await _adminService.AssignRoleAsync(userId, dto.Role);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return NoContent();
    }

    /// <summary>
    /// Removes a role from a user. Idempotent.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpDelete("users/{userId}/roles/{role}")]
    public async Task<IActionResult> RemoveRoleAsync(string userId, string role)
    {
        var result = await _adminService.RemoveRoleAsync(userId, role);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return NoContent();
    }

    /// <summary>
    /// Revokes all active sessions for a user (signs them out of all devices).
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpDelete("users/{userId}/sessions")]
    public async Task<IActionResult> RevokeAllUserSessionsAsync(string userId)
    {
        var result = await _adminService.RevokeAllUserSessionsAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return NoContent();
    }

    /// <summary>
    /// Revokes a single session for a user.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpDelete("users/{userId}/sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeUserSessionAsync(string userId, Guid sessionId)
    {
        var result = await _adminService.RevokeUserSessionAsync(userId, sessionId);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return NoContent();
    }

    // ── Background Jobs ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the status of all registered Quartz jobs (next/previous run, trigger state, cron).
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobsAsync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var runningJobs = await scheduler.GetCurrentlyExecutingJobs();

        var statuses = new List<JobStatusDto>();

        foreach (var jobKey in jobKeys.OrderBy(k => k.Name))
        {
            var detail = await scheduler.GetJobDetail(jobKey);
            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            var trigger = triggers.FirstOrDefault();

            string triggerState = "Unknown";
            DateTimeOffset? nextFire = null;
            DateTimeOffset? prevFire = null;
            string? cronExpression = null;

            if (trigger is not null)
            {
                var state = await scheduler.GetTriggerState(trigger.Key);
                triggerState = state.ToString();
                nextFire = trigger.GetNextFireTimeUtc();
                prevFire = trigger.GetPreviousFireTimeUtc();
                if (trigger is ICronTrigger cron)
                    cronExpression = cron.CronExpressionString;
            }

            var isRunning = runningJobs.Any(j => j.JobDetail.Key.Equals(jobKey));

            var lastRun = _jobHistory.Get(jobKey.Name);

            statuses.Add(new JobStatusDto(
                Name: jobKey.Name,
                Description: detail?.Description,
                TriggerState: triggerState,
                NextFireTimeUtc: nextFire,
                PreviousFireTimeUtc: prevFire,
                CronExpression: cronExpression,
                IsRunning: isRunning,
                LastRunUtc: lastRun?.CompletedAtUtc,
                LastRunStatus: lastRun?.Status,
                LastRunDetails: lastRun?.Details,
                LastRunDurationMs: lastRun?.DurationMs,
                LastRunTriggeredBy: lastRun?.TriggeredBy));
        }

        return Ok(statuses);
    }

    /// <summary>
    /// Immediately triggers a job by name (fires it outside its normal schedule).
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("jobs/{jobName}/trigger")]
    public async Task<IActionResult> TriggerJobAsync(string jobName)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(jobName);

        if (!await scheduler.CheckExists(jobKey))
            return NotFound(new { error = $"Job '{jobName}' not found." });

        var userId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Pass the triggering admin's userId into the job so the listener can attribute it
        var data = new JobDataMap { ["TriggeredBy"] = userId ?? "admin" };
        await scheduler.TriggerJob(jobKey, data);

        // Record immediately so the audit log shows who triggered it and when
        await _auditLogService.RecordAsync(
            userId: userId,
            action: AuditActions.JobTriggeredManually,
            entityType: "Job",
            entityId: jobName);

        return NoContent();
    }

    /// <summary>
    /// Returns a paginated audit log, filterable by userId, action, and date range.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var result = await _auditLogService.GetLogsAsync(page, pageSize, userId, action, from, to);
        if (result.IsFailure) return BadRequest(new { result.Error });
        return Ok(result.Data);
    }
}
