﻿// <auto-generated />
using System;
using HighlightBot;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HighlightBot.Migrations
{
    [DbContext(typeof(HighlightDbContext))]
    [Migration("20240623171436_RegexOptions")]
    partial class RegexOptions
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("HighlightBot.HighlightTerm", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Display")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Regex")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("RegexOptions")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(1);

                    b.Property<decimal>("user_serverid")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("user_userid")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("Id");

                    b.HasIndex("user_serverid", "user_userid");

                    b.ToTable("Terms");
                });

            modelBuilder.Entity("HighlightBot.HighlightUser", b =>
                {
                    b.Property<decimal>("DiscordGuildId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("DiscordUserId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<TimeSpan>("HighlightDelay")
                        .HasColumnType("interval");

                    b.Property<bool>("IgnoreBots")
                        .HasColumnType("boolean");

                    b.Property<bool>("IgnoreNsfw")
                        .HasColumnType("boolean");

                    b.Property<DateTime>("LastActivity")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime>("LastDM")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("DiscordGuildId", "DiscordUserId");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("HighlightBot.HighlightUserIgnoredChannel", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<decimal>("ChannelId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("UserDiscordGuildId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("UserDiscordUserId")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("Id");

                    b.HasIndex("UserDiscordGuildId", "UserDiscordUserId");

                    b.ToTable("HighlightUserIgnoredChannel");
                });

            modelBuilder.Entity("HighlightBot.HighlightUserIgnoredUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<decimal>("IgnoredUserId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("UserDiscordGuildId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("UserDiscordUserId")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("Id");

                    b.HasIndex("UserDiscordGuildId", "UserDiscordUserId");

                    b.ToTable("HighlightUserIgnoredUser");
                });

            modelBuilder.Entity("HighlightBot.HighlightTerm", b =>
                {
                    b.HasOne("HighlightBot.HighlightUser", "User")
                        .WithMany("Terms")
                        .HasForeignKey("user_serverid", "user_userid")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("HighlightBot.HighlightUserIgnoredChannel", b =>
                {
                    b.HasOne("HighlightBot.HighlightUser", "User")
                        .WithMany("IgnoredChannels")
                        .HasForeignKey("UserDiscordGuildId", "UserDiscordUserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("HighlightBot.HighlightUserIgnoredUser", b =>
                {
                    b.HasOne("HighlightBot.HighlightUser", "User")
                        .WithMany("IgnoredUsers")
                        .HasForeignKey("UserDiscordGuildId", "UserDiscordUserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("HighlightBot.HighlightUser", b =>
                {
                    b.Navigation("IgnoredChannels");

                    b.Navigation("IgnoredUsers");

                    b.Navigation("Terms");
                });
#pragma warning restore 612, 618
        }
    }
}
