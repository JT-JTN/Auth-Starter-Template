using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistance.Configurations;

public sealed class PasskeyCredentialConfiguration : IEntityTypeConfiguration<PasskeyCredential>
{
    public void Configure(EntityTypeBuilder<PasskeyCredential> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(p => p.CredentialId)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasIndex(p => p.CredentialId)
            .IsUnique();

        builder.Property(p => p.PublicKey)
            .IsRequired();

        builder.Property(p => p.SignCount)
            .IsRequired();

        builder.Property(p => p.AaGuid)
            .IsRequired();

        builder.Property(p => p.Transports)
            .HasMaxLength(512);

        builder.Property(p => p.Name)
            .HasMaxLength(256);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
