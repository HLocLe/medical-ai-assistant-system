using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedMateAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationReminderChannelSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderEmailSentAt",
                table: "ConsultationSession",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderPushSentAt",
                table: "ConsultationSession",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderScheduledAt",
                table: "ConsultationSession",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderEmailSentAt",
                table: "ConsultationSession");

            migrationBuilder.DropColumn(
                name: "ReminderPushSentAt",
                table: "ConsultationSession");

            migrationBuilder.DropColumn(
                name: "ReminderScheduledAt",
                table: "ConsultationSession");
        }
    }
}
