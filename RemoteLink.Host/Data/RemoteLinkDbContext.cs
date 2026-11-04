using Microsoft.EntityFrameworkCore;
using RemoteLink.Core.Models.Chat;

namespace RemoteLink.Host.Data
{
    public sealed class RemoteLinkDbContext(DbContextOptions<RemoteLinkDbContext> options) : DbContext(options)
    {
        public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
        
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

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
        }
    }
}