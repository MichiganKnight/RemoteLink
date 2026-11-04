using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Chat;
using RemoteLink.Core.Models.Transfers;

namespace RemoteLink.Host.Data
{
    public sealed class RemoteLinkDbContext(DbContextOptions<RemoteLinkDbContext> options) : DbContext(options)
    {
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        
        public DbSet<TransferJob> TransferJobs => Set<TransferJob>();

        public DbSet<TransferItem> TransferItems => Set<TransferItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ChatConversation>(entity =>
            {
                entity.ToTable("ChatConversations");

                entity.HasKey(conversation => conversation.Id);

                entity.HasIndex(conversation => conversation.RemoteDeviceId).IsUnique();

                entity.Property(conversation => conversation.CreatedUtc).IsRequired();

                entity.HasMany(conversation => conversation.Messages)
                    .WithOne(message => message.Conversation)
                    .HasForeignKey(message => message.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.ToTable("ChatMessages");

                entity.HasKey(message => message.Id);

                entity.Property(message => message.Content).HasMaxLength(10000)
                    .IsRequired();

                entity.Property(message => message.Direction).HasConversion<int>();

                entity.Property(message => message.Status).HasConversion<int>();

                entity.HasIndex(message => new
                {
                    message.ConversationId,
                    message.CreatedUtc,
                    message.Id
                });

                entity.HasIndex(message => new
                {
                    message.Direction,
                    message.Status,
                    message.CreatedUtc
                });
            });

            modelBuilder.Entity<TransferJob>(entity =>
            {
                entity.HasKey(t => t.Id);
                
                entity.Property(t => t.Name).IsRequired()
                    .HasMaxLength(255);

                entity.Property(t => t.Direction).HasConversion<int>();

                entity.Property(t => t.Status).HasConversion<int>();

                entity.Property(t => t.ErrorMessage).HasMaxLength(2048);

                entity.HasIndex(t => new
                {
                    t.RemoteDeviceId,
                    t.CreatedUtc
                });

                entity.HasIndex(t => t.Status);

                entity.HasMany(t => t.Items)
                    .WithOne(i => i.Transfer)
                    .HasForeignKey(i => i.TransferId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            
            modelBuilder.Entity<TransferItem>(entity =>
            {
                entity.HasKey(i => i.Id);

                entity.Property(i => i.RelativePath).IsRequired()
                    .HasMaxLength(2048);

                entity.Property(i => i.Sha256).HasMaxLength(64);

                entity.HasIndex(i => new
                {
                    i.TransferId,
                    i.RelativePath
                }).IsUnique();
            });
        }
    }
}