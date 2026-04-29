using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoiaProcessor.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoiaRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestorFullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestorOrganization = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RequestorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RequestorPhone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RequestorMailingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoiaRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FoiaRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_FoiaRequests_FoiaRequestId",
                        column: x => x.FoiaRequestId,
                        principalTable: "FoiaRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FoiaRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDocumentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceUri = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OriginalContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RedactedContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RedactionStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_FoiaRequests_FoiaRequestId",
                        column: x => x.FoiaRequestId,
                        principalTable: "FoiaRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleasePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FoiaRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZipBlobName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BlobContainerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SasUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SasExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleasePackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleasePackages_FoiaRequests_FoiaRequestId",
                        column: x => x.FoiaRequestId,
                        principalTable: "FoiaRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FoiaRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedReviewer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewerDecision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewerComments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewTasks_FoiaRequests_FoiaRequestId",
                        column: x => x.FoiaRequestId,
                        principalTable: "FoiaRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Redactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PiiType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReplacementText = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartOffset = table.Column<int>(type: "int", nullable: true),
                    EndOffset = table.Column<int>(type: "int", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    DetectionSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReviewerApproved = table.Column<bool>(type: "bit", nullable: true),
                    ReviewerComments = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Redactions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_FoiaRequestId_Timestamp",
                table: "AuditEvents",
                columns: new[] { "FoiaRequestId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FoiaRequestId",
                table: "Documents",
                column: "FoiaRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_FoiaRequests_Status",
                table: "FoiaRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Redactions_DocumentId",
                table: "Redactions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePackages_FoiaRequestId",
                table: "ReleasePackages",
                column: "FoiaRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewTasks_FoiaRequestId",
                table: "ReviewTasks",
                column: "FoiaRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "Redactions");

            migrationBuilder.DropTable(
                name: "ReleasePackages");

            migrationBuilder.DropTable(
                name: "ReviewTasks");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "FoiaRequests");
        }
    }
}
