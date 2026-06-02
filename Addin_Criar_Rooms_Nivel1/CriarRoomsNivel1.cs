using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CasaClienteCriarRoomsNivel1
{
    public class App : IExternalApplication
    {
        private const string TargetModelFileName = "Casa de Júlio Santa Barbara.rvt";
        private static readonly string ProjectFolder = @"C:\Users\ulete\Documents\Robô Modelador Bim\Projetos_BIM\Projetos_Ativos\Casa_Cliente_Atual";
        private static readonly string OutputFolder = Path.Combine(ProjectFolder, "Diagnostico_BIM");
        private static readonly string BackupFolder = Path.Combine(ProjectFolder, "Backups");
        private static readonly string TriggerFile = Path.Combine(OutputFolder, "EXECUTAR_CRIACAO_ROOMS_NIVEL1.trigger");
        private static readonly string ReportFile = Path.Combine(OutputFolder, "relatorio_criacao_rooms_nivel1.txt");
        private static readonly HashSet<string> ProcessedDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] RoomNames =
        {
            "Sala",
            "Cozinha",
            "Área de Serviço",
            "Quarto 01",
            "Quarto 02",
            "Suíte",
            "Closet",
            "Banheiro Social",
            "Banheiro Suíte"
        };

        public Result OnStartup(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ViewActivated += OnViewActivated;
            application.Idling += OnIdling;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ViewActivated -= OnViewActivated;
            application.Idling -= OnIdling;
            return Result.Succeeded;
        }

        private static void OnDocumentOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)
        {
            TryRun(args.Document);
        }

        private static void OnViewActivated(object sender, ViewActivatedEventArgs args)
        {
            TryRun(args.Document);
        }

        private static void OnIdling(object sender, IdlingEventArgs args)
        {
            UIApplication uiApplication = sender as UIApplication;
            TryRun(uiApplication?.ActiveUIDocument?.Document);
        }

        private static void TryRun(Document doc)
        {
            if (doc == null || doc.IsFamilyDocument || !IsTargetModel(doc) || !File.Exists(TriggerFile))
            {
                return;
            }

            string documentKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            if (!ProcessedDocuments.Add(documentKey))
            {
                return;
            }

            var lines = new List<string>
            {
                "RELATORIO - CRIACAO AUTOMATICA DE ROOMS NO NIVEL 1",
                "Data/Hora: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Modelo: " + Safe(doc.Title),
                "Caminho: " + Safe(doc.PathName),
                "",
                "Escopo autorizado:",
                "- Criar Rooms automaticamente para ambientes fechados do Nivel 1.",
                "- Nao alterar paredes.",
                "- Nao alterar portas.",
                "- Somente criar Rooms.",
                ""
            };

            try
            {
                Directory.CreateDirectory(OutputFolder);
                Directory.CreateDirectory(BackupFolder);

                string backupPath = CreateBackup(doc, lines);
                int existingRoomsBefore = CountRoomsOnLevel(doc, "Nível 1");
                lines.Add("Rooms existentes no Nivel 1 antes da execucao: " + existingRoomsBefore);

                if (existingRoomsBefore > 0)
                {
                    lines.Add("Acao cancelada: ja existem Rooms no Nivel 1. Nenhum Room novo foi criado.");
                    FinishTrigger("cancelado_rooms_existentes", lines);
                    WriteReport(lines);
                    return;
                }

                Level level = FindLevel(doc, "Nível 1");
                if (level == null)
                {
                    lines.Add("Acao cancelada: Nivel 1 nao encontrado.");
                    FinishTrigger("cancelado_nivel_nao_encontrado", lines);
                    WriteReport(lines);
                    return;
                }

                Phase phase = FindPhase(doc);
                PlanTopology topology = doc.get_PlanTopology(level);
                if (topology == null)
                {
                    lines.Add("Acao cancelada: topologia de planta do Nivel 1 nao encontrada.");
                    FinishTrigger("cancelado_sem_topologia", lines);
                    WriteReport(lines);
                    return;
                }

                var circuits = topology.Circuits.Cast<PlanCircuit>()
                    .Where(circuit => !circuit.IsRoomLocated)
                    .OrderByDescending(circuit => circuit.Area)
                    .ToList();

                lines.Add("Circuitos fechados sem Room encontrados: " + circuits.Count);
                lines.Add("Backup criado antes da transacao: " + backupPath);
                lines.Add("");

                int created = 0;
                using (Transaction transaction = new Transaction(doc, "Criar Rooms Nivel 1 - Codex"))
                {
                    transaction.Start();

                    foreach (PlanCircuit circuit in circuits)
                    {
                        if (created >= RoomNames.Length)
                        {
                            break;
                        }

                        Room room = doc.Create.NewRoom(phase);
                        SetRoomNameAndNumber(room, RoomNames[created], created + 1);
                        doc.Create.NewRoom(room, circuit);
                        lines.Add(string.Format(CultureInfo.InvariantCulture,
                            "Room criado: {0} | Numero: {1:00} | Area circuito aprox. m2: {2:0.###}",
                            RoomNames[created],
                            created + 1,
                            UnitUtils.ConvertFromInternalUnits(circuit.Area, UnitTypeId.SquareMeters)));
                        created++;
                    }

                    transaction.Commit();
                }

                lines.Add("");
                lines.Add("Rooms criados: " + created);
                if (created < RoomNames.Length)
                {
                    lines.Add("Aviso: foram encontrados menos circuitos fechados livres do que os 9 nomes informados.");
                }
                if (circuits.Count > RoomNames.Length)
                {
                    lines.Add("Aviso: existem circuitos fechados adicionais sem nome informado; eles nao foram preenchidos.");
                }
                lines.Add("Modelo modificado em memoria. O add-in nao executou Save.");

                FinishTrigger("concluido", lines);
                WriteReport(lines);
            }
            catch (Exception ex)
            {
                lines.Add("");
                lines.Add("ERRO: " + ex);
                FinishTrigger("erro", lines);
                WriteReport(lines);
            }
        }

        private static string CreateBackup(Document doc, IList<string> lines)
        {
            if (string.IsNullOrWhiteSpace(doc.PathName) || !File.Exists(doc.PathName))
            {
                lines.Add("Aviso: nao foi possivel criar backup porque o caminho do modelo nao esta disponivel.");
                return string.Empty;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string fileName = Path.GetFileNameWithoutExtension(doc.PathName) + "_backup_antes_criar_rooms_" + stamp + ".rvt";
            string backupPath = Path.Combine(BackupFolder, fileName);
            File.Copy(doc.PathName, backupPath, false);
            return backupPath;
        }

        private static void SetRoomNameAndNumber(Room room, string name, int number)
        {
            Parameter nameParameter = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            if (nameParameter != null && !nameParameter.IsReadOnly)
            {
                nameParameter.Set(name);
            }

            Parameter numberParameter = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            if (numberParameter != null && !numberParameter.IsReadOnly)
            {
                numberParameter.Set(number.ToString("00", CultureInfo.InvariantCulture));
            }
        }

        private static int CountRoomsOnLevel(Document doc, string levelName)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Count(room => string.Equals(Safe(doc.GetElement(room.LevelId)?.Name), levelName, StringComparison.OrdinalIgnoreCase));
        }

        private static Level FindLevel(Document doc, string levelName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(level => string.Equals(level.Name, levelName, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(RemoveAccents(level.Name), RemoveAccents(levelName), StringComparison.OrdinalIgnoreCase));
        }

        private static Phase FindPhase(Document doc)
        {
            return doc.Phases.Cast<Phase>().FirstOrDefault(phase =>
                       RemoveAccents(phase.Name).IndexOf("Construcao nova", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? doc.Phases.Cast<Phase>().LastOrDefault();
        }

        private static bool IsTargetModel(Document doc)
        {
            if (!string.IsNullOrWhiteSpace(doc.PathName) &&
                string.Equals(Path.GetFileName(doc.PathName), TargetModelFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (doc.Title ?? string.Empty).IndexOf("Casa de Júlio Santa Barbara", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void FinishTrigger(string status, IList<string> lines)
        {
            try
            {
                if (File.Exists(TriggerFile))
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    string donePath = Path.Combine(OutputFolder, "EXECUTAR_CRIACAO_ROOMS_NIVEL1." + status + "." + stamp + ".txt");
                    File.Move(TriggerFile, donePath);
                    lines.Add("Gatilho finalizado: " + donePath);
                }
            }
            catch (Exception ex)
            {
                lines.Add("Aviso: nao foi possivel finalizar o gatilho: " + ex.Message);
            }
        }

        private static void WriteReport(IEnumerable<string> lines)
        {
            File.WriteAllLines(ReportFile, lines, new UTF8Encoding(true));
        }

        private static string RemoveAccents(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (char c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }
    }
}
