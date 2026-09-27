using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedMateAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class retrymechanismchoquotafinalizeholdreleasedconsumeauditlogchofreequota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FreeQuotaUsageId",
                table: "SymptomAnalysisSession",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotaSource",
                table: "SymptomAnalysisSession",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "SymptomAnalysisSession",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FreeQuotaUsage",
                columns: table => new
                {
                    FreeQuotaUsageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LimitValue = table.Column<int>(type: "integer", nullable: false),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeQuotaUsage", x => x.FreeQuotaUsageId);
                    table.CheckConstraint("CK_FreeQuotaUsage_Counts", "\"UsedCount\" >= 0 AND \"ReservedCount\" >= 0 AND \"UsedCount\" + \"ReservedCount\" <= \"LimitValue\"");
                    table.CheckConstraint("CK_FreeQuotaUsage_LimitValue", "\"LimitValue\" >= 0");
                    table.ForeignKey(
                        name: "FK_FreeQuotaUsage_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FreeQuotaUsageLog",
                columns: table => new
                {
                    FreeQuotaUsageLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreeQuotaUsageId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UsedCountBefore = table.Column<int>(type: "integer", nullable: false),
                    UsedCountAfter = table.Column<int>(type: "integer", nullable: false),
                    ReservedCountBefore = table.Column<int>(type: "integer", nullable: false),
                    ReservedCountAfter = table.Column<int>(type: "integer", nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeQuotaUsageLog", x => x.FreeQuotaUsageLogId);
                    table.CheckConstraint("CK_FreeQuotaUsageLog_Counts", "\"UsedCountBefore\" >= 0 AND \"UsedCountAfter\" >= 0 AND \"ReservedCountBefore\" >= 0 AND \"ReservedCountAfter\" >= 0");
                    table.ForeignKey(
                        name: "FK_FreeQuotaUsageLog_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FreeQuotaUsageLog_FreeQuotaUsage_FreeQuotaUsageId",
                        column: x => x.FreeQuotaUsageId,
                        principalTable: "FreeQuotaUsage",
                        principalColumn: "FreeQuotaUsageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SymptomAnalysisSession_FreeQuotaUsageId",
                table: "SymptomAnalysisSession",
                column: "FreeQuotaUsageId");

            migrationBuilder.CreateIndex(
                name: "IX_SymptomAnalysisSession_Status_SubmittedAt_CreatedAt",
                table: "SymptomAnalysisSession",
                columns: new[] { "Status", "SubmittedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeQuotaUsage_UserId_Feature_BusinessDate",
                table: "FreeQuotaUsage",
                columns: new[] { "UserId", "Feature", "BusinessDate" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_FreeQuotaUsageLog_FreeQuotaUsageId_CreatedAt",
                table: "FreeQuotaUsageLog",
                columns: new[] { "FreeQuotaUsageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeQuotaUsageLog_IdempotencyKey",
                table: "FreeQuotaUsageLog",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FreeQuotaUsageLog_ReferenceType_ReferenceId",
                table: "FreeQuotaUsageLog",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeQuotaUsageLog_UserId_CreatedAt",
                table: "FreeQuotaUsageLog",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_SymptomAnalysisSession_FreeQuotaUsage_FreeQuotaUsageId",
                table: "SymptomAnalysisSession",
                column: "FreeQuotaUsageId",
                principalTable: "FreeQuotaUsage",
                principalColumn: "FreeQuotaUsageId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SymptomAnalysisSession_FreeQuotaUsage_FreeQuotaUsageId",
                table: "SymptomAnalysisSession");

            migrationBuilder.DropTable(
                name: "FreeQuotaUsageLog");

            migrationBuilder.DropTable(
                name: "FreeQuotaUsage");

            migrationBuilder.DropIndex(
                name: "IX_SymptomAnalysisSession_FreeQuotaUsageId",
                table: "SymptomAnalysisSession");

            migrationBuilder.DropIndex(
                name: "IX_SymptomAnalysisSession_Status_SubmittedAt_CreatedAt",
                table: "SymptomAnalysisSession");

            migrationBuilder.DropColumn(
                name: "FreeQuotaUsageId",
                table: "SymptomAnalysisSession");

            migrationBuilder.DropColumn(
                name: "QuotaSource",
                table: "SymptomAnalysisSession");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "SymptomAnalysisSession");
        }
    }
}
