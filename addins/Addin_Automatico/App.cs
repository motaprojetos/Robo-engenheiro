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

namespace CasaClienteDiagnosticoAuto
{
    public class App : IExternalApplication
    {
        private const string TargetModelFileName = "Casa de Júlio Santa Barbara.rvt";
        private static readonly string OutputFolder = @"C:\Users\ulete\Documents\Robô Modelador Bim\Projetos_BIM\Projetos_Ativos\Casa_Cliente_Atual\Diagnostico_BIM";
        private static readonly HashSet<string> ProcessedDocuments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Result OnStartup(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ViewActivated += OnViewActivated;
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ViewActivated -= OnViewActivated;
            return Result.Succeeded;
        }

        private static void OnDocumentOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)
        {
            TryExport(args.Document);
        }

        private static void OnViewActivated(object sender, ViewActivatedEventArgs args)
        {
            TryExport(args.Document);
        }

        private static void TryExport(Document doc)
        {
            if (doc == null || doc.IsFamilyDocument || !IsTargetModel(doc))
            {
                return;
            }

            string documentKey = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
            if (!ProcessedDocuments.Add(documentKey))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(OutputFolder))
                {
                    Directory.CreateDirectory(OutputFolder);
                }

                var exportedFiles = new List<string>();
                exportedFiles.Add(ExportProjectInformation(doc));
                exportedFiles.Add(ExportLevels(doc));
                exportedFiles.Add(ExportViews(doc));
                exportedFiles.Add(ExportSheets(doc));
                exportedFiles.Add(ExportRooms(doc));
                exportedFiles.Add(ExportAreas(doc));
                exportedFiles.Add(ExportWalls(doc));
                exportedFiles.Add(ExportFamilyInstances(doc, BuiltInCategory.OST_Doors, "08_doors.csv"));
                exportedFiles.Add(ExportFamilyInstances(doc, BuiltInCategory.OST_Windows, "09_windows.csv"));
                exportedFiles.Add(ExportWarnings(doc));
                exportedFiles.Add(ExportCadLinks(doc));
                exportedFiles.Add(ExportRvtLinks(doc));
                exportedFiles.Add(ExportSummary(doc, exportedFiles));
                AppendLog("Diagnostico concluido para: " + Safe(doc.Title));
            }
            catch (Exception ex)
            {
                AppendLog("Falha no diagnostico: " + ex);
            }
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

        private static string ExportProjectInformation(Document doc)
        {
            var rows = new List<string[]>
            {
                new[] { "Campo", "Valor" },
                new[] { "Titulo do Documento", Safe(doc.Title) },
                new[] { "Caminho do Arquivo", Safe(doc.PathName) },
                new[] { "Arquivo de Familia", doc.IsFamilyDocument ? "Sim" : "Nao" },
                new[] { "Worksharing", doc.IsWorkshared ? "Ativado" : "Nao ativado" }
            };

            ProjectInfo info = doc.ProjectInformation;
            if (info != null)
            {
                rows.Add(new[] { "Nome do Projeto", Safe(info.Name) });
                rows.Add(new[] { "Numero do Projeto", Safe(info.Number) });
                rows.Add(new[] { "Cliente", Safe(info.ClientName) });
                rows.Add(new[] { "Endereco", Safe(info.Address) });
                rows.Add(new[] { "Status", Safe(info.Status) });
                rows.Add(new[] { "Data de Emissao", Safe(info.IssueDate) });

                foreach (Parameter parameter in info.Parameters)
                {
                    Definition definition = parameter.Definition;
                    if (definition == null)
                    {
                        continue;
                    }
                    rows.Add(new[] { "Parametro: " + Safe(definition.Name), Safe(ParameterValue(parameter)) });
                }
            }

            return WriteCsv("01_project_information.csv", rows);
        }

        private static string ExportLevels(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Nome", "Elevacao_m" } };
            foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation))
            {
                rows.Add(new[] { IdValue(level.Id), Safe(level.Name), Meters(level.Elevation) });
            }
            return WriteCsv("02_levels.csv", rows);
        }

        private static string ExportViews(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Nome", "Tipo", "Template", "Escala", "Folha" } };
            foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().OrderBy(x => x.ViewType.ToString()).ThenBy(x => x.Name))
            {
                rows.Add(new[]
                {
                    IdValue(view.Id),
                    Safe(view.Name),
                    Safe(view.ViewType.ToString()),
                    view.IsTemplate ? "Sim" : "Nao",
                    view.Scale.ToString(CultureInfo.InvariantCulture),
                    Safe(GetSheetNumberForView(doc, view.Id))
                });
            }
            return WriteCsv("03_views.csv", rows);
        }

        private static string ExportSheets(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Numero", "Nome", "Revisao Atual" } };
            foreach (ViewSheet sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().OrderBy(x => x.SheetNumber))
            {
                rows.Add(new[]
                {
                    IdValue(sheet.Id),
                    Safe(sheet.SheetNumber),
                    Safe(sheet.Name),
                    Safe(sheet.LookupParameter("Current Revision")?.AsValueString() ?? sheet.LookupParameter("Revisao atual")?.AsValueString())
                });
            }
            return WriteCsv("04_sheets.csv", rows);
        }

        private static string ExportRooms(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Numero", "Nome", "Nivel", "Area_m2", "Perimetro_m", "Fase" } };
            foreach (Room room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>().OrderBy(x => x.Number).ThenBy(x => x.Name))
            {
                rows.Add(new[]
                {
                    IdValue(room.Id),
                    Safe(room.Number),
                    Safe(room.Name),
                    Safe(GetElementName(doc, room.LevelId)),
                    SquareMeters(room.Area),
                    Meters(room.Perimeter),
                    Safe(GetElementName(doc, room.CreatedPhaseId))
                });
            }
            return WriteCsv("05_rooms.csv", rows);
        }

        private static string ExportAreas(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Numero", "Nome", "Nivel", "Area_m2", "Esquema" } };
            foreach (SpatialElement area in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<SpatialElement>().OrderBy(x => x.Name))
            {
                rows.Add(new[]
                {
                    IdValue(area.Id),
                    Safe(GetParameterValue(area, BuiltInParameter.ROOM_NUMBER)),
                    Safe(area.Name),
                    Safe(GetElementName(doc, area.LevelId)),
                    SquareMeters(GetDoubleParameter(area, BuiltInParameter.ROOM_AREA)),
                    Safe(GetParameterValue(area, BuiltInParameter.AREA_SCHEME_NAME))
                });
            }
            return WriteCsv("06_areas.csv", rows);
        }

        private static string ExportWalls(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Tipo", "Nivel Base", "Nivel Topo", "Comprimento_m", "Area_m2", "Volume_m3", "Fase Criacao", "Fase Demolicao" } };
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().Where(x => x.WallType != null).OrderBy(x => x.WallType.Name))
            {
                rows.Add(new[]
                {
                    IdValue(wall.Id),
                    Safe(wall.WallType.Name),
                    Safe(GetElementName(doc, wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId())),
                    Safe(GetElementName(doc, wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId())),
                    Meters(GetDoubleParameter(wall, BuiltInParameter.CURVE_ELEM_LENGTH)),
                    SquareMeters(GetDoubleParameter(wall, BuiltInParameter.HOST_AREA_COMPUTED)),
                    CubicMeters(GetDoubleParameter(wall, BuiltInParameter.HOST_VOLUME_COMPUTED)),
                    Safe(GetElementName(doc, wall.CreatedPhaseId)),
                    Safe(GetElementName(doc, wall.DemolishedPhaseId))
                });
            }
            return WriteCsv("07_walls.csv", rows);
        }

        private static string ExportFamilyInstances(Document doc, BuiltInCategory category, string fileName)
        {
            var rows = new List<string[]> { new[] { "Id", "Familia", "Tipo", "Nivel", "Ambiente De", "Ambiente Para", "Fase Criacao", "Fase Demolicao", "Largura_m", "Altura_m" } };
            foreach (FamilyInstance instance in new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().Cast<FamilyInstance>().OrderBy(x => x.Symbol.FamilyName).ThenBy(x => x.Name))
            {
                rows.Add(new[]
                {
                    IdValue(instance.Id),
                    Safe(instance.Symbol?.FamilyName),
                    Safe(instance.Symbol?.Name),
                    Safe(GetElementName(doc, instance.LevelId)),
                    Safe(instance.FromRoom?.Name),
                    Safe(instance.ToRoom?.Name),
                    Safe(GetElementName(doc, instance.CreatedPhaseId)),
                    Safe(GetElementName(doc, instance.DemolishedPhaseId)),
                    Meters(GetDoubleParameter(instance.Symbol, BuiltInParameter.DOOR_WIDTH)),
                    Meters(GetDoubleParameter(instance.Symbol, BuiltInParameter.DOOR_HEIGHT))
                });
            }
            return WriteCsv(fileName, rows);
        }

        private static string ExportWarnings(Document doc)
        {
            var rows = new List<string[]> { new[] { "Indice", "Descricao", "Elementos" } };
            int index = 1;
            foreach (FailureMessage warning in doc.GetWarnings())
            {
                string elements = string.Join(";", warning.GetFailingElements().Select(IdValue));
                rows.Add(new[] { index.ToString(), Safe(warning.GetDescriptionText()), elements });
                index++;
            }
            return WriteCsv("10_warnings.csv", rows);
        }

        private static string ExportCadLinks(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Nome", "Importado ou Vinculado", "Vista Especifica", "Arquivo" } };
            foreach (ImportInstance cad in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().OrderBy(x => x.Name))
            {
                CADLinkType type = doc.GetElement(cad.GetTypeId()) as CADLinkType;
                rows.Add(new[]
                {
                    IdValue(cad.Id),
                    Safe(cad.Name),
                    cad.IsLinked ? "Vinculado" : "Importado",
                    cad.ViewSpecific ? "Sim" : "Nao",
                    Safe(type?.Name)
                });
            }

            AddTransmissionRows(doc, rows, "CADLink");
            return WriteCsv("11_cad_links.csv", rows);
        }

        private static string ExportRvtLinks(Document doc)
        {
            var rows = new List<string[]> { new[] { "Id", "Nome", "Tipo", "Status" } };
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().OrderBy(x => x.Name))
            {
                RevitLinkType type = doc.GetElement(link.GetTypeId()) as RevitLinkType;
                rows.Add(new[]
                {
                    IdValue(link.Id),
                    Safe(link.Name),
                    Safe(type?.Name),
                    Safe(type == null ? string.Empty : type.GetLinkedFileStatus().ToString())
                });
            }

            AddTransmissionRows(doc, rows, "RevitLink");
            return WriteCsv("12_rvt_links.csv", rows);
        }

        private static string ExportSummary(Document doc, IList<string> exportedFiles)
        {
            int rooms = Count(doc, BuiltInCategory.OST_Rooms);
            int areas = Count(doc, BuiltInCategory.OST_Areas);
            int walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().Count();
            int doors = Count(doc, BuiltInCategory.OST_Doors);
            int windows = Count(doc, BuiltInCategory.OST_Windows);
            int warnings = doc.GetWarnings().Count;
            int cadLinks = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).WhereElementIsNotElementType().Count();
            int rvtLinks = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).WhereElementIsNotElementType().Count();

            var lines = new List<string>
            {
                "DIAGNOSTICO BIM - SOMENTE LEITURA",
                "Modelo: " + Safe(doc.Title),
                "Caminho: " + Safe(doc.PathName),
                "Data/Hora: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                "Modo: leitura, sem Transaction, sem salvar o modelo.",
                "",
                "Quantidades:",
                "Rooms: " + rooms,
                "Areas: " + areas,
                "Walls: " + walls,
                "Doors: " + doors,
                "Windows: " + windows,
                "Warnings: " + warnings,
                "CAD Links/Imports: " + cadLinks,
                "RVT Links: " + rvtLinks,
                "",
                "Arquivos gerados:"
            };
            lines.AddRange(exportedFiles.Select(Path.GetFileName));

            string path = Path.Combine(OutputFolder, "00_resumo_diagnostico_bim.txt");
            File.WriteAllLines(path, lines, new UTF8Encoding(true));
            return path;
        }

        private static int Count(Document doc, BuiltInCategory category)
        {
            return new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().Count();
        }

        private static void AddTransmissionRows(Document doc, IList<string[]> rows, string typeFilter)
        {
            if (string.IsNullOrWhiteSpace(doc.PathName))
            {
                return;
            }

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(doc.PathName);
                TransmissionData data = TransmissionData.ReadTransmissionData(modelPath);
                if (data == null)
                {
                    return;
                }

                foreach (ElementId id in data.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference reference = data.GetLastSavedReferenceData(id);
                    if (reference == null || reference.ExternalFileReferenceType.ToString().IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    rows.Add(new[]
                    {
                        IdValue(id),
                        "TransmissionData",
                        Safe(reference.ExternalFileReferenceType.ToString()),
                        Safe(reference.GetLinkedFileStatus().ToString()),
                        Safe(ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetPath()))
                    });
                }
            }
            catch (Exception ex)
            {
                rows.Add(new[] { string.Empty, "TransmissionData", "Erro", Safe(ex.Message), string.Empty });
            }
        }

        private static string GetSheetNumberForView(Document doc, ElementId viewId)
        {
            foreach (ViewSheet sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                if (sheet.GetAllPlacedViews().Contains(viewId))
                {
                    return sheet.SheetNumber;
                }
            }
            return string.Empty;
        }

        private static string GetElementName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
            {
                return string.Empty;
            }
            return Safe(doc.GetElement(id)?.Name);
        }

        private static string GetParameterValue(Element element, BuiltInParameter builtInParameter)
        {
            return Safe(ParameterValue(element?.get_Parameter(builtInParameter)));
        }

        private static double GetDoubleParameter(Element element, BuiltInParameter builtInParameter)
        {
            Parameter parameter = element?.get_Parameter(builtInParameter);
            if (parameter == null || parameter.StorageType != StorageType.Double)
            {
                return 0.0;
            }
            return parameter.AsDouble();
        }

        private static string ParameterValue(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
            {
                return string.Empty;
            }

            string valueString = parameter.AsValueString();
            if (!string.IsNullOrWhiteSpace(valueString))
            {
                return valueString;
            }

            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    return parameter.AsDouble().ToString("0.########", CultureInfo.InvariantCulture);
                case StorageType.Integer:
                    return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.ElementId:
                    return IdValue(parameter.AsElementId());
                default:
                    return string.Empty;
            }
        }

        private static string WriteCsv(string fileName, IEnumerable<string[]> rows)
        {
            string path = Path.Combine(OutputFolder, fileName);
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                foreach (string[] row in rows)
                {
                    writer.WriteLine(string.Join(",", row.Select(Csv)));
                }
            }
            return path;
        }

        private static string Csv(string value)
        {
            value = value ?? string.Empty;
            bool quote = value.Contains(",") || value.Contains("\"") || value.Contains("\r") || value.Contains("\n");
            value = value.Replace("\"", "\"\"");
            return quote ? "\"" + value + "\"" : value;
        }

        private static string Safe(string value)
        {
            return value ?? string.Empty;
        }

        private static string IdValue(ElementId id)
        {
            if (id == null)
            {
                return string.Empty;
            }
            return id.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Meters(double internalValue)
        {
            return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Meters).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string SquareMeters(double internalValue)
        {
            return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.SquareMeters).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string CubicMeters(double internalValue)
        {
            return UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.CubicMeters).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void AppendLog(string message)
        {
            string path = Path.Combine(OutputFolder, "diagnostico_addin_log.txt");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " - " + message + Environment.NewLine, new UTF8Encoding(true));
        }
    }
}
