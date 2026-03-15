using Application;
using Application.DTOs.Admin;
using Application.DTOs.Auth;
using Application.Services;
using Domain.Users;
using Infrastructure.Persistance;
using Infrastructure.Persistance.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Infrastructure.Services;

public sealed class AdminService : IAdminService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ITokenRepository _tokenRepository;
    private readonly IUnitOfWork _uow;
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTime;
    private readonly IAuditLogService _audit;
    private readonly IRealtimeNotifier _notifier;

    public AdminService(
        UserManager<User> userManager,
        RoleManager<IdentityRole> roleManager,
        ITokenRepository tokenRepository,
        IUnitOfWork uow,
        ApplicationDbContext db,
        IDateTimeProvider dateTime,
        IAuditLogService audit,
        IRealtimeNotifier notifier)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tokenRepository = tokenRepository;
        _uow = uow;
        _db = db;
        _dateTime = dateTime;
        _audit = audit;
        _notifier = notifier;
    }

    public async Task<PaginatedResultWithStatus<AdminUserSummaryDto>> GetUsersAsync(int page, int pageSize, string? search)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.Email ?? "").ToLower().Contains(s) ||
                u.FirstName.ToLower().Contains(s) ||
                u.LastName.ToLower().Contains(s));
        }

        var totalCount = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Batch load roles for all returned users in a single query
        var userIds = users.Select(u => u.Id).ToList();
        var roleMap = await (
            from ur in _db.UserRoles
            join r in _db.Roles on ur.RoleId equals r.Id
            where userIds.Contains(ur.UserId)
            select new { ur.UserId, r.Name }
        ).ToListAsync();

        var rolesByUser = roleMap
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var dtos = users.Select(u => new AdminUserSummaryDto(
            u.Id,
            u.FirstName,
            u.LastName,
            u.Email ?? "",
            u.EmailConfirmed,
            rolesByUser.TryGetValue(u.Id, out var roles) ? roles : [],
            u.CreatedAtUtc,
            u.LastLoginUtc,
            u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow
        )).ToList();

        return PaginatedResultWithStatus<AdminUserSummaryDto>.Success(dtos, page, pageSize, totalCount);
    }

    public async Task<Result<AdminUserDetailDto>> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure<AdminUserDetailDto>("User not found");

        var roles = await _userManager.GetRolesAsync(user);
        var activeSessions = await _tokenRepository.GetActiveSessionsForUserAsync(userId);

        var sessionDtos = activeSessions.Select(t => new SessionDto(
            t.Id, t.IpAddress, t.UserAgent, t.CreatedAtUtc, t.LastUsedUtc, IsCurrent: false
        )).ToList();

        var dto = new AdminUserDetailDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email ?? "",
            user.EmailConfirmed,
            user.PhoneNumber,
            roles.ToList(),
            user.CreatedAtUtc,
            user.LastLoginUtc,
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            sessionDtos
        );

        return Result.Success(dto);
    }

    public async Task<Result> AssignRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        if (!await _roleManager.RoleExistsAsync(role))
            return Result.Failure($"Role '{role}' does not exist");

        if (await _userManager.IsInRoleAsync(user, role))
            return Result.Success(); // Already assigned — idempotent

        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
            return Result.Failure(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.RecordAsync(userId, AuditActions.RoleAssigned, entityType: "Role", entityId: role);
        await _notifier.NotifyUserAsync(userId,
            "Role Assigned",
            $"The '{role}' role has been granted to your account by an administrator.");
        return Result.Success();
    }

    public async Task<Result> RemoveRoleAsync(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        if (!await _userManager.IsInRoleAsync(user, role))
            return Result.Success(); // Not in role — idempotent

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            return Result.Failure(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _audit.RecordAsync(userId, AuditActions.RoleRemoved, entityType: "Role", entityId: role);
        await _notifier.NotifyUserAsync(userId,
            "Role Removed",
            $"The '{role}' role has been removed from your account by an administrator.",
            "warning");
        return Result.Success();
    }

    public async Task<Result> RevokeUserSessionAsync(string userId, Guid sessionId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        var token = await _tokenRepository.GetActiveTokenByIdAsync(sessionId, userId);
        if (token is null)
            return Result.Failure("Session not found or already revoked");

        token.RevokedAtUtc = _dateTime.UtcNow;
        await _tokenRepository.UpdateRefreshTokenAsync(token);
        await _uow.SaveChangesAsync();
        await _audit.RecordAsync(userId, AuditActions.SessionRevoked, entityType: "Session", entityId: sessionId.ToString());
        await _notifier.NotifyUserAsync(userId,
            "Session Revoked",
            "One of your active sessions was revoked by an administrator.",
            "warning");
        return Result.Success();
    }

    public async Task<Result> RevokeAllUserSessionsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        await _tokenRepository.DeleteAllRefreshTokensForUserAsync(userId);
        await _uow.SaveChangesAsync();
        await _audit.RecordAsync(userId, AuditActions.AllSessionsRevoked);
        await _notifier.NotifyUserAsync(userId,
            "All Sessions Revoked",
            "All your active sessions have been revoked by an administrator.",
            "error");
        return Result.Success();
    }
}
