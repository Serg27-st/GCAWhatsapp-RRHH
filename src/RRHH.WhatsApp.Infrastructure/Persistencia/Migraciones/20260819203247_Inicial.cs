using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RRHH.WhatsApp.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Analistas",
                columns: table => new
                {
                    AnalistaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rol = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Analistas", x => x.AnalistaId);
                });

            migrationBuilder.CreateTable(
                name: "Auditoria",
                columns: table => new
                {
                    AuditoriaId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntidadTipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntidadId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AnalistaId = table.Column<int>(type: "int", nullable: true),
                    Accion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auditoria", x => x.AuditoriaId);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracionReglas",
                columns: table => new
                {
                    Clave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionReglas", x => x.Clave);
                });

            migrationBuilder.CreateTable(
                name: "Cuentas",
                columns: table => new
                {
                    CuentaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cuentas", x => x.CuentaId);
                });

            migrationBuilder.CreateTable(
                name: "EtapasKanban",
                columns: table => new
                {
                    EtapaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    EsFinal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtapasKanban", x => x.EtapaId);
                });

            migrationBuilder.CreateTable(
                name: "EventosSistema",
                columns: table => new
                {
                    EventoId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    IntentosProcesamiento = table.Column<int>(type: "int", nullable: false),
                    UltimoError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaProcesado = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosSistema", x => x.EventoId);
                });

            migrationBuilder.CreateTable(
                name: "Plantillas",
                columns: table => new
                {
                    PlantillaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Clave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NombreMeta = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Categoria = table.Column<int>(type: "int", nullable: false),
                    Idioma = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TextoAprobado = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CantidadParametros = table.Column<int>(type: "int", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plantillas", x => x.PlantillaId);
                });

            migrationBuilder.CreateTable(
                name: "Postulantes",
                columns: table => new
                {
                    PostulanteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Dni = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NombreCompleto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TelefonoUltimo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postulantes", x => x.PostulanteId);
                });

            migrationBuilder.CreateTable(
                name: "Ausencias",
                columns: table => new
                {
                    AusenciaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnalistaId = table.Column<int>(type: "int", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ausencias", x => x.AusenciaId);
                    table.ForeignKey(
                        name: "FK_Ausencias_Analistas_AnalistaId",
                        column: x => x.AnalistaId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnalistaCuenta",
                columns: table => new
                {
                    AnalistaCuentaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnalistaId = table.Column<int>(type: "int", nullable: false),
                    CuentaId = table.Column<int>(type: "int", nullable: false),
                    EsBackup = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalistaCuenta", x => x.AnalistaCuentaId);
                    table.ForeignKey(
                        name: "FK_AnalistaCuenta_Analistas_AnalistaId",
                        column: x => x.AnalistaId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalistaCuenta_Cuentas_CuentaId",
                        column: x => x.CuentaId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HC",
                columns: table => new
                {
                    HcId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CuentaId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    CodigoJobForms = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UrlJobForms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCierre = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HC", x => x.HcId);
                    table.ForeignKey(
                        name: "FK_HC_Cuentas_CuentaId",
                        column: x => x.CuentaId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HorarioAtencion",
                columns: table => new
                {
                    HorarioId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CuentaId = table.Column<int>(type: "int", nullable: true),
                    DiaSemana = table.Column<int>(type: "int", nullable: false),
                    HoraInicio = table.Column<TimeOnly>(type: "time", nullable: false),
                    HoraFin = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorarioAtencion", x => x.HorarioId);
                    table.ForeignKey(
                        name: "FK_HorarioAtencion_Cuentas_CuentaId",
                        column: x => x.CuentaId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversaciones",
                columns: table => new
                {
                    ConversacionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelefonoE164 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PostulanteId = table.Column<int>(type: "int", nullable: true),
                    CuentaContextoId = table.Column<int>(type: "int", nullable: true),
                    AnalistaAtendiendoId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    FechaOptIn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrigenOptIn = table.Column<int>(type: "int", nullable: true),
                    FechaUltimoMensajeEntrante = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaUltimaRespuestaAnalista = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaUltimaActividad = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversaciones", x => x.ConversacionId);
                    table.ForeignKey(
                        name: "FK_Conversaciones_Analistas_AnalistaAtendiendoId",
                        column: x => x.AnalistaAtendiendoId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversaciones_Cuentas_CuentaContextoId",
                        column: x => x.CuentaContextoId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Conversaciones_Postulantes_PostulanteId",
                        column: x => x.PostulanteId,
                        principalTable: "Postulantes",
                        principalColumn: "PostulanteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EstadosPostulanteCuenta",
                columns: table => new
                {
                    EstadoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostulanteId = table.Column<int>(type: "int", nullable: false),
                    CuentaId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AnalistaId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadosPostulanteCuenta", x => x.EstadoId);
                    table.ForeignKey(
                        name: "FK_EstadosPostulanteCuenta_Analistas_AnalistaId",
                        column: x => x.AnalistaId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstadosPostulanteCuenta_Cuentas_CuentaId",
                        column: x => x.CuentaId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstadosPostulanteCuenta_Postulantes_PostulanteId",
                        column: x => x.PostulanteId,
                        principalTable: "Postulantes",
                        principalColumn: "PostulanteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HCCamposOpcionales",
                columns: table => new
                {
                    CampoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HcId = table.Column<int>(type: "int", nullable: false),
                    NombreCampo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HCCamposOpcionales", x => x.CampoId);
                    table.ForeignKey(
                        name: "FK_HCCamposOpcionales_HC_HcId",
                        column: x => x.HcId,
                        principalTable: "HC",
                        principalColumn: "HcId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Postulaciones",
                columns: table => new
                {
                    PostulacionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostulanteId = table.Column<int>(type: "int", nullable: false),
                    HcId = table.Column<int>(type: "int", nullable: false),
                    CuentaId = table.Column<int>(type: "int", nullable: false),
                    AnalistaAsignadoId = table.Column<int>(type: "int", nullable: true),
                    EtapaKanbanId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaActividad = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCambioEtapa = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postulaciones", x => x.PostulacionId);
                    table.ForeignKey(
                        name: "FK_Postulaciones_Analistas_AnalistaAsignadoId",
                        column: x => x.AnalistaAsignadoId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Postulaciones_Cuentas_CuentaId",
                        column: x => x.CuentaId,
                        principalTable: "Cuentas",
                        principalColumn: "CuentaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Postulaciones_EtapasKanban_EtapaKanbanId",
                        column: x => x.EtapaKanbanId,
                        principalTable: "EtapasKanban",
                        principalColumn: "EtapaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Postulaciones_HC_HcId",
                        column: x => x.HcId,
                        principalTable: "HC",
                        principalColumn: "HcId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Postulaciones_Postulantes_PostulanteId",
                        column: x => x.PostulanteId,
                        principalTable: "Postulantes",
                        principalColumn: "PostulanteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobFormsInvitaciones",
                columns: table => new
                {
                    InvitacionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversacionId = table.Column<int>(type: "int", nullable: false),
                    PostulanteId = table.Column<int>(type: "int", nullable: true),
                    HcId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FechaEnvioLink = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordatorioEnviado = table.Column<bool>(type: "bit", nullable: false),
                    FechaRecordatorio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AvisoAnalistaEnviado = table.Column<bool>(type: "bit", nullable: false),
                    FechaAvisoAnalista = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Completado = table.Column<bool>(type: "bit", nullable: false),
                    FechaCompletado = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFormsInvitaciones", x => x.InvitacionId);
                    table.ForeignKey(
                        name: "FK_JobFormsInvitaciones_Conversaciones_ConversacionId",
                        column: x => x.ConversacionId,
                        principalTable: "Conversaciones",
                        principalColumn: "ConversacionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobFormsInvitaciones_HC_HcId",
                        column: x => x.HcId,
                        principalTable: "HC",
                        principalColumn: "HcId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobFormsInvitaciones_Postulantes_PostulanteId",
                        column: x => x.PostulanteId,
                        principalTable: "Postulantes",
                        principalColumn: "PostulanteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Mensajes",
                columns: table => new
                {
                    MensajeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversacionId = table.Column<int>(type: "int", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Direccion = table.Column<int>(type: "int", nullable: false),
                    Contenido = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    PlantillaId = table.Column<int>(type: "int", nullable: true),
                    AnalistaId = table.Column<int>(type: "int", nullable: true),
                    FechaEnvio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstadoEntrega = table.Column<int>(type: "int", nullable: false),
                    ErrorProveedor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mensajes", x => x.MensajeId);
                    table.ForeignKey(
                        name: "FK_Mensajes_Analistas_AnalistaId",
                        column: x => x.AnalistaId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mensajes_Conversaciones_ConversacionId",
                        column: x => x.ConversacionId,
                        principalTable: "Conversaciones",
                        principalColumn: "ConversacionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Mensajes_Plantillas_PlantillaId",
                        column: x => x.PlantillaId,
                        principalTable: "Plantillas",
                        principalColumn: "PlantillaId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transferencias",
                columns: table => new
                {
                    TransferenciaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversacionId = table.Column<int>(type: "int", nullable: false),
                    PostulacionId = table.Column<int>(type: "int", nullable: true),
                    AnalistaOrigenId = table.Column<int>(type: "int", nullable: false),
                    AnalistaDestinoId = table.Column<int>(type: "int", nullable: false),
                    Urgente = table.Column<bool>(type: "bit", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Comentario = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaRespuesta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transferencias", x => x.TransferenciaId);
                    table.ForeignKey(
                        name: "FK_Transferencias_Analistas_AnalistaDestinoId",
                        column: x => x.AnalistaDestinoId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transferencias_Analistas_AnalistaOrigenId",
                        column: x => x.AnalistaOrigenId,
                        principalTable: "Analistas",
                        principalColumn: "AnalistaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transferencias_Conversaciones_ConversacionId",
                        column: x => x.ConversacionId,
                        principalTable: "Conversaciones",
                        principalColumn: "ConversacionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transferencias_Postulaciones_PostulacionId",
                        column: x => x.PostulacionId,
                        principalTable: "Postulaciones",
                        principalColumn: "PostulacionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobFormsRespuestas",
                columns: table => new
                {
                    RespuestaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvitacionId = table.Column<int>(type: "int", nullable: true),
                    PostulanteId = table.Column<int>(type: "int", nullable: false),
                    HcId = table.Column<int>(type: "int", nullable: false),
                    DatosJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CvUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConsentimientoAceptado = table.Column<bool>(type: "bit", nullable: false),
                    VersionAvisoPrivacidad = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FechaConsentimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaEnvio = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFormsRespuestas", x => x.RespuestaId);
                    table.ForeignKey(
                        name: "FK_JobFormsRespuestas_HC_HcId",
                        column: x => x.HcId,
                        principalTable: "HC",
                        principalColumn: "HcId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobFormsRespuestas_JobFormsInvitaciones_InvitacionId",
                        column: x => x.InvitacionId,
                        principalTable: "JobFormsInvitaciones",
                        principalColumn: "InvitacionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobFormsRespuestas_Postulantes_PostulanteId",
                        column: x => x.PostulanteId,
                        principalTable: "Postulantes",
                        principalColumn: "PostulanteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ConfiguracionReglas",
                columns: new[] { "Clave", "Descripcion", "FechaActualizacion", "Valor" },
                values: new object[,]
                {
                    { "conversacion.archivado_dias", "Regla 16: dias sin actividad y sin marca de contratado o reingreso antes del archivado definitivo.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "90" },
                    { "conversacion.repregunta_dias", "Regla 9: dias de inactividad tras los cuales el bot vuelve a preguntar la empresa.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { "datos.retencion_cv_dias", "Regla 17: dias de retencion de CVs antes de la purga, alineado a la Ley de Proteccion de Datos Personales.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "365" },
                    { "datos.version_aviso_privacidad", "Regla 17: version del aviso de privacidad vigente, que se sella en cada respuesta de JobForms.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2026-08-v1" },
                    { "envio.maximo_por_segundo", "Seccion 9.6.4: tope de velocidad de envio saliente hacia Meta, para no repetir el patron que causo el bloqueo.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "10" },
                    { "escalamiento.horas", "Regla 2: horas sin respuesta del titular antes de escalar al respaldo fijo.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "2" },
                    { "escalamiento.solo_horario_laboral", "Si es true, esas horas solo corren dentro del horario de atencion, para que un mensaje recibido al cierre no escale de madrugada.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "jobforms.aviso_analista_horas", "Regla 9: horas tras enviar el link antes de avisarle al analista que el postulante no completo el formulario.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "48" },
                    { "jobforms.recordatorio_horas", "Regla 9: horas tras enviar el link antes de recordarle al postulante que complete el formulario.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "24" },
                    { "outbox.reintentos_maximos", "Seccion 9.6.2: intentos de procesamiento de un evento de la outbox antes de marcarlo como fallido.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5" }
                });

            migrationBuilder.InsertData(
                table: "EtapasKanban",
                columns: new[] { "EtapaId", "EsFinal", "Nombre", "Orden" },
                values: new object[,]
                {
                    { 1, false, "Postulante nuevo", 1 },
                    { 2, false, "En revision", 2 },
                    { 3, false, "Entrevista", 3 },
                    { 4, true, "Contratado", 4 },
                    { 5, true, "Descartado", 5 }
                });

            migrationBuilder.InsertData(
                table: "Plantillas",
                columns: new[] { "PlantillaId", "Activa", "CantidadParametros", "Categoria", "Clave", "Idioma", "NombreMeta", "TextoAprobado" },
                values: new object[,]
                {
                    { 1, false, 1, 1, "fuera_horario", "es", "rrhh_fuera_horario", "Hola. Nuestro horario de atencion es {{1}}. Tu mensaje ya quedo registrado y un analista te respondera apenas retomemos la jornada." },
                    { 2, false, 3, 1, "recordatorio_24h", "es", "rrhh_recordatorio_formulario", "Hola {{1}}. Notamos que aun no completas tu ficha de postulacion para la vacante {{2}}. Puedes hacerlo aqui: {{3}}" },
                    { 3, false, 2, 1, "confirmacion_jobforms", "es", "rrhh_confirmacion_formulario", "Gracias {{1}}. Recibimos tu ficha para la vacante {{2}}. Un analista revisara tu postulacion y te escribira por este mismo chat." },
                    { 4, false, 2, 1, "cierre_cortesia", "es", "rrhh_cierre_cortesia", "Hola {{1}}. Agradecemos tu interes en la vacante {{2}}. En esta oportunidad continuaremos con otros perfiles, pero mantendremos tus datos para futuras convocatorias." },
                    { 5, false, 1, 1, "vacante_cerrada", "es", "rrhh_vacante_cerrada", "La vacante {{1}} ya fue cubierta. Puedes revisar nuestras otras convocatorias activas respondiendo a este mensaje." },
                    { 6, false, 1, 1, "reapertura_conversacion", "es", "rrhh_reapertura", "Hola {{1}}. Vemos que vuelves a escribirnos. Para ayudarte mejor, indicanos a que empresa corresponde tu consulta." }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalistaCuenta_AnalistaId_CuentaId",
                table: "AnalistaCuenta",
                columns: new[] { "AnalistaId", "CuentaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalistaCuenta_RespaldoUnicoPorCuenta",
                table: "AnalistaCuenta",
                column: "CuentaId",
                unique: true,
                filter: "[EsBackup] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Analistas_Email",
                table: "Analistas",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_EntidadTipo_EntidadId_Fecha",
                table: "Auditoria",
                columns: new[] { "EntidadTipo", "EntidadId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Ausencias_AnalistaId_FechaInicio_FechaFin",
                table: "Ausencias",
                columns: new[] { "AnalistaId", "FechaInicio", "FechaFin" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_AnalistaAtendiendoId_Estado",
                table: "Conversaciones",
                columns: new[] { "AnalistaAtendiendoId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_CuentaContextoId",
                table: "Conversaciones",
                column: "CuentaContextoId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_Estado_FechaUltimaRespuestaAnalista",
                table: "Conversaciones",
                columns: new[] { "Estado", "FechaUltimaRespuestaAnalista" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_PostulanteId",
                table: "Conversaciones",
                column: "PostulanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversaciones_TelefonoE164",
                table: "Conversaciones",
                column: "TelefonoE164",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cuentas_Nombre",
                table: "Cuentas",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstadosPostulanteCuenta_AnalistaId",
                table: "EstadosPostulanteCuenta",
                column: "AnalistaId");

            migrationBuilder.CreateIndex(
                name: "IX_EstadosPostulanteCuenta_CuentaId",
                table: "EstadosPostulanteCuenta",
                column: "CuentaId");

            migrationBuilder.CreateIndex(
                name: "IX_EstadosPostulanteCuenta_PostulanteId_CuentaId_Tipo",
                table: "EstadosPostulanteCuenta",
                columns: new[] { "PostulanteId", "CuentaId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_EtapasKanban_Orden",
                table: "EtapasKanban",
                column: "Orden",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosSistema_CorrelationId",
                table: "EventosSistema",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosSistema_Estado_FechaCreacion",
                table: "EventosSistema",
                columns: new[] { "Estado", "FechaCreacion" });

            migrationBuilder.CreateIndex(
                name: "IX_HC_CuentaId_Estado",
                table: "HC",
                columns: new[] { "CuentaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_HCCamposOpcionales_HcId",
                table: "HCCamposOpcionales",
                column: "HcId");

            migrationBuilder.CreateIndex(
                name: "IX_HorarioAtencion_CuentaId_DiaSemana",
                table: "HorarioAtencion",
                columns: new[] { "CuentaId", "DiaSemana" });

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_Completado_AvisoAnalistaEnviado_FechaEnvioLink",
                table: "JobFormsInvitaciones",
                columns: new[] { "Completado", "AvisoAnalistaEnviado", "FechaEnvioLink" });

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_Completado_RecordatorioEnviado_FechaEnvioLink",
                table: "JobFormsInvitaciones",
                columns: new[] { "Completado", "RecordatorioEnviado", "FechaEnvioLink" });

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_ConversacionId",
                table: "JobFormsInvitaciones",
                column: "ConversacionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_HcId",
                table: "JobFormsInvitaciones",
                column: "HcId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_PostulanteId",
                table: "JobFormsInvitaciones",
                column: "PostulanteId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsInvitaciones_Token",
                table: "JobFormsInvitaciones",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsRespuestas_HcId",
                table: "JobFormsRespuestas",
                column: "HcId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsRespuestas_InvitacionId",
                table: "JobFormsRespuestas",
                column: "InvitacionId");

            migrationBuilder.CreateIndex(
                name: "IX_JobFormsRespuestas_PostulanteId_HcId",
                table: "JobFormsRespuestas",
                columns: new[] { "PostulanteId", "HcId" });

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_AnalistaId",
                table: "Mensajes",
                column: "AnalistaId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_ConversacionId_FechaEnvio",
                table: "Mensajes",
                columns: new[] { "ConversacionId", "FechaEnvio" });

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_CorrelationId",
                table: "Mensajes",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_PlantillaId",
                table: "Mensajes",
                column: "PlantillaId");

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_ProviderMessageId",
                table: "Mensajes",
                column: "ProviderMessageId",
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Plantillas_Clave",
                table: "Plantillas",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_AnalistaAsignadoId_CuentaId_Estado",
                table: "Postulaciones",
                columns: new[] { "AnalistaAsignadoId", "CuentaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_CuentaId",
                table: "Postulaciones",
                column: "CuentaId");

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_Estado_FechaUltimaActividad",
                table: "Postulaciones",
                columns: new[] { "Estado", "FechaUltimaActividad" });

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_EtapaKanbanId",
                table: "Postulaciones",
                column: "EtapaKanbanId");

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_HcId_EtapaKanbanId",
                table: "Postulaciones",
                columns: new[] { "HcId", "EtapaKanbanId" });

            migrationBuilder.CreateIndex(
                name: "IX_Postulaciones_PostulanteId_HcId",
                table: "Postulaciones",
                columns: new[] { "PostulanteId", "HcId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Postulantes_Dni",
                table: "Postulantes",
                column: "Dni",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_AnalistaDestinoId_Estado",
                table: "Transferencias",
                columns: new[] { "AnalistaDestinoId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_AnalistaOrigenId",
                table: "Transferencias",
                column: "AnalistaOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_ConversacionId",
                table: "Transferencias",
                column: "ConversacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_PostulacionId",
                table: "Transferencias",
                column: "PostulacionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalistaCuenta");

            migrationBuilder.DropTable(
                name: "Auditoria");

            migrationBuilder.DropTable(
                name: "Ausencias");

            migrationBuilder.DropTable(
                name: "ConfiguracionReglas");

            migrationBuilder.DropTable(
                name: "EstadosPostulanteCuenta");

            migrationBuilder.DropTable(
                name: "EventosSistema");

            migrationBuilder.DropTable(
                name: "HCCamposOpcionales");

            migrationBuilder.DropTable(
                name: "HorarioAtencion");

            migrationBuilder.DropTable(
                name: "JobFormsRespuestas");

            migrationBuilder.DropTable(
                name: "Mensajes");

            migrationBuilder.DropTable(
                name: "Transferencias");

            migrationBuilder.DropTable(
                name: "JobFormsInvitaciones");

            migrationBuilder.DropTable(
                name: "Plantillas");

            migrationBuilder.DropTable(
                name: "Postulaciones");

            migrationBuilder.DropTable(
                name: "Conversaciones");

            migrationBuilder.DropTable(
                name: "EtapasKanban");

            migrationBuilder.DropTable(
                name: "HC");

            migrationBuilder.DropTable(
                name: "Analistas");

            migrationBuilder.DropTable(
                name: "Postulantes");

            migrationBuilder.DropTable(
                name: "Cuentas");
        }
    }
}
