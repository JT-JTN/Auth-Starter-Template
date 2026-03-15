using Application.Services;
using Domain.Users;
using Infrastructure.Options;
using Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Services;

/// <summary>
/// Stores profile images as binary data in the SQL Server database.
/// Images are served via GET /api/users/{userId}/profile-image.
/// </summary>
public sealed class DatabaseProfileImageStore : IProfileImageStore
{
    private const int MaxImageSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly ApplicationDbContext _context;
    private readonly AppSettings _appSettings;
    private readonly IDateTimeProvider _dateTime;

    public DatabaseProfileImageStore(
        ApplicationDbContext context,
        IOptions<AppSettings> appSettings,
        IDateTimeProvider dateTime)
    {
        _context = context;
        _appSettings = appSettings.Value;
        _dateTime = dateTime;
    }

    public async Task<string> SaveAsync(string userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        var data = ms.ToArray();

        if (data.Length > MaxImageSizeBytes)
            throw new InvalidOperationException($"Image exceeds maximum size of {MaxImageSizeBytes / 1024 / 1024} MB.");

        var existing = await _context.UserProfileImages.FindAsync([userId], ct);
        if (existing is null)
        {
            _context.UserProfileImages.Add(new UserProfileImage
            {
                UserId = userId,
                Data = data,
                ContentType = contentType,
                UpdatedAtUtc = _dateTime.UtcNow
            });
        }
        else
        {
            existing.Data = data;
            existing.ContentType = contentType;
            existing.UpdatedAtUtc = _dateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        var baseUrl = _appSettings.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/v1/users/{userId}/profile-image";
    }

    public async Task<(byte[] Data, string ContentType)?> GetAsync(string userId, CancellationToken ct = default)
    {
        var image = await _context.UserProfileImages
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.UserId == userId, ct);

        return image is null ? null : (image.Data, image.ContentType);
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var image = await _context.UserProfileImages.FindAsync([userId], ct);
        if (image is not null)
        {
            _context.UserProfileImages.Remove(image);
            await _context.SaveChangesAsync(ct);
        }
    }
}
