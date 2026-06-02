using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CasaClienteCorrigirRoomsNivel1
{
    public class App : IExternalApplication
    {
        private const string TargetModelFileName = "Casa de Júlio Santa Barbara.rvt";
        private static readonly string ProjectFolder = @"C:\Users\ulete\Documents\Robô Modelador Bim\Projetos_BIM\Projetos_Ativos\Casa_Cliente_Atual";
        private static readonly string OutputFolder = Path.Combine(ProjectFolder, "Diagnostico_BIM");
        private static readonly string BackupFolder = Path.Combine(ProjectFolder, "Backups");
        private static readonly string TriggerFile = Path.Combine(OutputFolder, "EXECUTAR_CORRECAO_NOMES_ROOMS_NIVEL1.trigger");
        private static readonly string ReportFile = Path.Combine(OutputFolder, "relatorio_correcao_rooms_nivel1.txt");
        private static readonly HashSet<string> ProcessedDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            string key = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            if (!ProcessedDocuments.Add(key))
            {
                return;
            }

            var lines = new List<string>
            {
                "RELATORIO - CORRECAO DE NOMES DOS ROOMS NO NIVEL 1",
                "Data/Hora: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Modelo: " + Safe(doc.Title),
                "Caminho: " + Safe(doc.PathName),
                "",
                "Escopo autorizado:",
                "- Corrigir nomes/numeros de Rooms existentes.",
                "- Remover somente o Room externo/grande chamado Sala.",
                "- Nao alterar paredes.",
                "- Nao alterar portas.",
                "- Nao alterar janelas.",
                ""
            };

            try
            {
                Directory.CreateDirectory(OutputFolder);
                Directory.CreateDirectory(BackupFolder);
                string backupPath = CreateBackup(doc, lines);
                lines.Add("Backup criado antes da transacao: " + backupPath);
                lines.Add("");

                var rooms = GetRoomsOnLevel(doc, "Nível 1").ToList();
                var originalRooms = rooms.Select(room => new RoomSnapshot(room, RoomName(room), RoomNumber(room), room.Area)).ToList();
                lines.Add("Rooms encontrados no Nivel 1 antes da correcao: " + rooms.Count);
                foreach (RoomSnapshot snapshot in originalRooms.OrderByDescending(r => r.Area))
                {
                    lines.Add("Antes: Id=" + IdValue(snapshot.Room.Id) + " | Numero=" + Safe(snapshot.Number) + " | Nome=" + Safe(snapshot.Name) + " | Area_m2=" + SquareMeters(snapshot.Area));
                }
                lines.Add("");

                using (Transaction transaction = new Transaction(doc, "Corrigir Rooms Nivel 1 - Codex"))
                {
                    transaction.Start();

                    RoomSnapshot externalRoom = originalRooms
                        .Where(room => Same(room.Name, "Sala"))
                        .OrderByDescending(room => room.Area)
                        .FirstOrDefault(room => UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters) > 100.0);

                    if (externalRoom != null)
                    {
                        string externalRoomId = externalRoom.IdText;
                        lines.Add("Room removido: Id=" + IdValue(externalRoom.Room.Id) + " | Nome=" + Safe(externalRoom.Name) + " | Area_m2=" + SquareMeters(externalRoom.Area));
                        doc.Delete(externalRoom.Room.Id);
                        var editableRooms = originalRooms.Where(snapshot => snapshot.IdText != externalRoomId).ToList();

                        RenameByOriginalName(editableRooms, "Área de Serviço", "Sala", "01", lines);
                        RenameByOriginalName(editableRooms, "Quarto 02", "Cozinha", "02", lines);
                        RenameByOriginalName(editableRooms, "Quarto 01", "Quarto 01", "03", lines);
                        RenameByOriginalName(editableRooms, "Suíte", "Quarto 02", "04", lines);
                        RenameByOriginalName(editableRooms, "Cozinha", "Suíte", "05", lines);
                        RenameByOriginalName(editableRooms, "Banheiro Suíte", "Closet", "06", lines);
                        RenameByOriginalName(editableRooms, "Closet", "Banheiro Social", "07", lines);
                        RenameByOriginalName(editableRooms, "Banheiro Social", "Banheiro Suíte", "08", lines);
                    }
                    else
                    {
                        lines.Add("Aviso: Room externo/grande chamado Sala nao encontrado para remocao.");
                        RenameByOriginalName(originalRooms, "Área de Serviço", "Sala", "01", lines);
                        RenameByOriginalName(originalRooms, "Quarto 02", "Cozinha", "02", lines);
                        RenameByOriginalName(originalRooms, "Quarto 01", "Quarto 01", "03", lines);
                        RenameByOriginalName(originalRooms, "Suíte", "Quarto 02", "04", lines);
                        RenameByOriginalName(originalRooms, "Cozinha", "Suíte", "05", lines);
                        RenameByOriginalName(originalRooms, "Banheiro Suíte", "Closet", "06", lines);
                        RenameByOriginalName(originalRooms, "Closet", "Banheiro Social", "07", lines);
                        RenameByOriginalName(originalRooms, "Banheiro Social", "Banheiro Suíte", "08", lines);
                    }

                    transaction.Commit();
                }

                lines.Add("");
                var afterRooms = GetRoomsOnLevel(doc, "Nível 1").OrderBy(r => r.Number).ToList();
                lines.Add("Rooms no Nivel 1 apos a correcao: " + afterRooms.Count);
                foreach (Room room in afterRooms)
                {
                    lines.Add("Depois: Id=" + IdValue(room.Id) + " | Numero=" + Safe(RoomNumber(room)) + " | Nome=" + Safe(RoomName(room)) + " | Area_m2=" + SquareMeters(room.Area));
                }
                lines.Add("");
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

        private static IEnumerable<Room> GetRoomsOnLevel(Document doc, string levelName)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(room => Same(doc.GetElement(room.LevelId)?.Name, levelName));
        }

        private static void RenameByOriginalName(IEnumerable<RoomSnapshot> rooms, string currentName, string newName, string newNumber, IList<string> lines)
        {
            RoomSnapshot snapshot = rooms.FirstOrDefault(candidate => Same(candidate.Name, currentName));
            if (snapshot == null)
            {
                lines.Add("Aviso: Room nao encontrado para renomear: " + currentName + " -> " + newName);
                return;
            }

            Room room = snapshot.Room;
            string oldName = Safe(snapshot.Name);
            string oldNumber = Safe(snapshot.Number);
            SetParameter(room, BuiltInParameter.ROOM_NAME, newName);
            SetParameter(room, BuiltInParameter.ROOM_NUMBER, newNumber);
            lines.Add("Renomeado: Id=" + IdValue(room.Id) + " | " + oldNumber + " " + oldName + " -> " + newNumber + " " + newName + " | Area_m2=" + SquareMeters(room.Area));
        }

        private static string RoomName(Room room)
        {
            return room?.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
        }

        private static string RoomNumber(Room room)
        {
            return room?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty;
        }

        private static void SetParameter(Room room, BuiltInParameter parameterId, string value)
        {
            Parameter parameter = room.get_Parameter(parameterId);
            if (parameter != null && !parameter.IsReadOnly)
            {
                parameter.Set(value);
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
            string backupPath = Path.Combine(BackupFolder, Path.GetFileNameWithoutExtension(doc.PathName) + "_backup_antes_corrigir_rooms_" + stamp + ".rvt");
            File.Copy(doc.PathName, backupPath, false);
            return backupPath;
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
                    string donePath = Path.Combine(OutputFolder, "EXECUTAR_CORRECAO_NOMES_ROOMS_NIVEL1." + status + "." + stamp + ".txt");
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

        private static bool Same(string left, string right)
        {
            return string.Equals(RemoveAccents(Safe(left)), RemoveAccents(Safe(right)), StringComparison.OrdinalIgnoreCase);
        }

        private static string RemoveAccents(string value)
        {
            string normalized = Safe(value).Normalize(NormalizationForm.FormD);
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

        private static string SquareMeters(double internalValue)
        {
            return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.SquareMeters).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string IdValue(ElementId id)
        {
            return id == null ? string.Empty : id.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }

        private class RoomSnapshot
        {
            public RoomSnapshot(Room room, string name, string number, double area)
            {
                Room = room;
                IdText = App.IdValue(room.Id);
                Name = name;
                Number = number;
                Area = area;
            }

            public Room Room { get; }
            public string IdText { get; }
            public string Name { get; }
            public string Number { get; }
            public double Area { get; }
        }
    }
}
