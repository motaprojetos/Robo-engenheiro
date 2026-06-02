# -*- coding: utf-8 -*-
"""
Dynamo 3.6 CPython3 + Revit 2026
Diagnostico BIM somente leitura.

Uso:
1. Abrir o modelo no Revit 2026.
2. Abrir Dynamo 3.6.
3. Criar um no Python configurado para CPython3.
4. Colar ou carregar este script no no Python.
5. Executar.

Garantias:
- Nao inicia Transaction.
- Nao altera elementos.
- Nao salva o modelo.
- Apenas le o documento ativo e grava CSVs na pasta OUT_DIR.
"""

import csv
import os
import traceback

import clr

clr.AddReference("RevitAPI")
clr.AddReference("RevitServices")

from Autodesk.Revit.DB import (  # noqa: E402
    BuiltInCategory,
    BuiltInParameter,
    ElementId,
    FilteredElementCollector,
    ImportInstance,
    Level,
    ModelPathUtils,
    RevitLinkInstance,
    StorageType,
    TransmissionData,
    UnitTypeId,
    UnitUtils,
    View,
    ViewSheet,
    Wall,
)
from RevitServices.Persistence import DocumentManager  # noqa: E402


doc = DocumentManager.Instance.CurrentDBDocument

OUT_DIR = (
    r"C:\Users\ulete\Documents\Robô Modelador Bim"
    r"\Projetos_BIM\Projetos_Ativos\Casa_Cliente_Atual\Diagnostico_BIM"
)


def ensure_dir(path):
    if not os.path.isdir(path):
        os.makedirs(path)


def safe_text(value):
    if value is None:
        return ""
    try:
        text = str(value)
    except Exception:
        try:
            text = value.ToString()
        except Exception:
            text = ""
    return text.replace("\r", " ").replace("\n", " ").strip()


def element_id_value(element_id):
    if element_id is None:
        return ""
    try:
        return element_id.IntegerValue
    except Exception:
        return safe_text(element_id)


def param_to_text(param):
    if param is None:
        return ""
    try:
        value = param.AsValueString()
        if value:
            return safe_text(value)
    except Exception:
        pass
    try:
        if param.StorageType == StorageType.String:
            return safe_text(param.AsString())
        if param.StorageType == StorageType.Integer:
            return safe_text(param.AsInteger())
        if param.StorageType == StorageType.Double:
            return safe_text(param.AsDouble())
        if param.StorageType == StorageType.ElementId:
            return safe_text(param.AsElementId().IntegerValue)
    except Exception:
        pass
    return ""


def lookup_param(element, *names):
    if element is None:
        return ""
    for name in names:
        try:
            param = element.LookupParameter(name)
            if param:
                return param_to_text(param)
        except Exception:
            pass
    return ""


def bip(element, builtin_parameter):
    if element is None or builtin_parameter is None:
        return ""
    try:
        return param_to_text(element.get_Parameter(builtin_parameter))
    except Exception:
        return ""


def get_bip(name):
    try:
        return getattr(BuiltInParameter, name)
    except Exception:
        return None


def convert_length_to_m(value):
    try:
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Meters)
    except Exception:
        try:
            return value * 0.3048
        except Exception:
            return 0


def convert_area_to_m2(value):
    try:
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.SquareMeters)
    except Exception:
        try:
            return value * 0.09290304
        except Exception:
            return 0


def get_type_name(element):
    try:
        type_id = element.GetTypeId()
        if type_id and type_id != ElementId.InvalidElementId:
            type_el = doc.GetElement(type_id)
            if type_el:
                return safe_text(type_el.Name)
    except Exception:
        pass
    return ""


def get_family_and_type(element):
    family = ""
    type_name = get_type_name(element)
    try:
        symbol = element.Symbol
        if symbol:
            type_name = safe_text(symbol.Name)
            family = safe_text(symbol.Family.Name)
    except Exception:
        pass
    return family, type_name


def category_name(element):
    try:
        if element.Category:
            return safe_text(element.Category.Name)
    except Exception:
        pass
    return ""


def write_csv(filename, headers, rows):
    ensure_dir(OUT_DIR)
    path = os.path.join(OUT_DIR, filename)
    with open(path, "w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.writer(stream)
        writer.writerow(headers)
        for row in rows:
            writer.writerow([safe_text(value) for value in row])
    return path


def collect_project_info():
    rows = []
    info = doc.ProjectInformation
    if not info:
        return rows

    rows.append(["ElementId", element_id_value(info.Id)])
    rows.append(["Name", safe_text(info.Name)])
    rows.append(["Number", safe_text(info.Number)])
    rows.append(["ClientName", safe_text(info.ClientName)])
    rows.append(["Address", safe_text(info.Address)])
    rows.append(["Status", safe_text(info.Status)])
    rows.append(["IssueDate", safe_text(info.IssueDate)])

    try:
        for param in info.Parameters:
            rows.append([safe_text(param.Definition.Name), param_to_text(param)])
    except Exception:
        pass
    return rows


def collect_levels():
    rows = []
    levels = list(FilteredElementCollector(doc).OfClass(Level).ToElements())
    levels.sort(key=lambda level: level.Elevation)
    for level in levels:
        rows.append([
            element_id_value(level.Id),
            level.Name,
            "{:.3f}".format(convert_length_to_m(level.Elevation)),
            level.Elevation,
        ])
    return rows


def collect_views():
    rows = []
    views = list(FilteredElementCollector(doc).OfClass(View).ToElements())
    views.sort(key=lambda view: safe_text(view.Name))
    for view in views:
        try:
            view_type = safe_text(view.ViewType)
        except Exception:
            view_type = ""
        try:
            scale = view.Scale
        except Exception:
            scale = ""
        try:
            is_template = view.IsTemplate
        except Exception:
            is_template = ""
        rows.append([
            element_id_value(view.Id),
            view.Name,
            view_type,
            scale,
            is_template,
            category_name(view),
        ])
    return rows


def collect_sheets():
    rows = []
    sheets = list(FilteredElementCollector(doc).OfClass(ViewSheet).ToElements())
    sheets.sort(key=lambda sheet: safe_text(sheet.SheetNumber))
    for sheet in sheets:
        rows.append([
            element_id_value(sheet.Id),
            sheet.SheetNumber,
            sheet.Name,
            sheet.IsPlaceholder,
        ])
    return rows


def collect_rooms():
    rows = []
    rooms = list(
        FilteredElementCollector(doc)
        .OfCategory(BuiltInCategory.OST_Rooms)
        .WhereElementIsNotElementType()
        .ToElements()
    )
    rooms.sort(key=lambda room: (lookup_param(room, "Número", "Number"), safe_text(room.Name)))
    for room in rooms:
        area_value = 0
        try:
            area_value = room.Area
        except Exception:
            pass
        try:
            level_name = room.Level.Name if room.Level else ""
        except Exception:
            level_name = ""
        rows.append([
            element_id_value(room.Id),
            lookup_param(room, "Número", "Number"),
            room.Name,
            level_name,
            "{:.2f}".format(convert_area_to_m2(area_value)) if area_value else "",
            lookup_param(room, "Volume"),
            lookup_param(room, "Fase", "Phase"),
        ])
    return rows


def collect_areas():
    rows = []
    areas = list(
        FilteredElementCollector(doc)
        .OfCategory(BuiltInCategory.OST_Areas)
        .WhereElementIsNotElementType()
        .ToElements()
    )
    areas.sort(key=lambda area: safe_text(area.Name))
    for area in areas:
        area_value = 0
        try:
            area_value = area.Area
        except Exception:
            pass
        try:
            level_name = area.Level.Name if area.Level else ""
        except Exception:
            level_name = ""
        rows.append([
            element_id_value(area.Id),
            lookup_param(area, "Número", "Number"),
            area.Name,
            level_name,
            "{:.2f}".format(convert_area_to_m2(area_value)) if area_value else "",
            lookup_param(area, "Esquema de área", "Area Scheme"),
        ])
    return rows


def collect_walls():
    rows = []
    walls = list(
        FilteredElementCollector(doc)
        .OfClass(Wall)
        .WhereElementIsNotElementType()
        .ToElements()
    )
    for wall in walls:
        rows.append([
            element_id_value(wall.Id),
            wall.Name,
            get_type_name(wall),
            bip(wall, BuiltInParameter.WALL_BASE_CONSTRAINT),
            bip(wall, BuiltInParameter.WALL_HEIGHT_TYPE),
            bip(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM),
            bip(wall, BuiltInParameter.CURVE_ELEM_LENGTH),
            bip(wall, BuiltInParameter.PHASE_CREATED),
            bip(wall, BuiltInParameter.PHASE_DEMOLISHED),
        ])
    return rows


def collect_family_instances(category):
    rows = []
    elements = list(
        FilteredElementCollector(doc)
        .OfCategory(category)
        .WhereElementIsNotElementType()
        .ToElements()
    )
    for element in elements:
        family, type_name = get_family_and_type(element)
        rows.append([
            element_id_value(element.Id),
            family,
            type_name,
            element.Name,
            bip(element, BuiltInParameter.FAMILY_LEVEL_PARAM),
            bip(element, BuiltInParameter.PHASE_CREATED),
            bip(element, BuiltInParameter.PHASE_DEMOLISHED),
            bip(element, get_bip("INSTANCE_SILL_HEIGHT_PARAM")),
            bip(element, get_bip("DOOR_NUMBER")),
        ])
    return rows


def collect_warnings():
    rows = []
    try:
        warnings = list(doc.GetWarnings())
    except Exception:
        warnings = []

    for warning in warnings:
        try:
            failing_ids = [safe_text(element_id_value(x)) for x in warning.GetFailingElements()]
        except Exception:
            failing_ids = []
        try:
            additional_ids = [safe_text(element_id_value(x)) for x in warning.GetAdditionalElements()]
        except Exception:
            additional_ids = []
        rows.append([
            warning.GetDescriptionText(),
            warning.GetSeverity(),
            ";".join(failing_ids),
            ";".join(additional_ids),
        ])
    return rows


def model_path_to_text(model_path):
    try:
        return ModelPathUtils.ConvertModelPathToUserVisiblePath(model_path)
    except Exception:
        return safe_text(model_path)


def external_ref_to_row(ref_id, ext_ref):
    if ext_ref is None:
        return [element_id_value(ref_id), "", "", "", "", "", ""]

    try:
        ref_type = safe_text(ext_ref.ExternalFileReferenceType)
    except Exception:
        ref_type = ""
    try:
        path = model_path_to_text(ext_ref.GetPath())
    except Exception:
        path = ""
    try:
        absolute_path = model_path_to_text(ext_ref.GetAbsolutePath())
    except Exception:
        absolute_path = ""
    try:
        path_type = safe_text(ext_ref.PathType)
    except Exception:
        path_type = ""
    try:
        status = safe_text(ext_ref.GetLinkedFileStatus())
    except Exception:
        status = ""

    return [
        element_id_value(ref_id),
        ref_type,
        path,
        absolute_path,
        path_type,
        status,
        "",
    ]


def collect_links():
    cad_rows = []
    rvt_rows = []

    try:
        if doc.PathName:
            model_path = ModelPathUtils.ConvertUserVisiblePathToModelPath(doc.PathName)
            transmission_data = TransmissionData.ReadTransmissionData(model_path)
        else:
            transmission_data = None
    except Exception:
        transmission_data = None

    if transmission_data:
        for ref_id in transmission_data.GetAllExternalFileReferenceIds():
            try:
                ext_ref = transmission_data.GetLastSavedReferenceData(ref_id)
            except Exception:
                ext_ref = None
            row = external_ref_to_row(ref_id, ext_ref)
            if "CAD" in row[1]:
                cad_rows.append(row)
            elif "Revit" in row[1]:
                rvt_rows.append(row)

    # Confirmacao por instancias presentes no documento ativo.
    for inst in FilteredElementCollector(doc).OfClass(ImportInstance).ToElements():
        try:
            linked = inst.IsLinked
        except Exception:
            linked = ""
        cad_rows.append([
            element_id_value(inst.Id),
            "ImportInstance/CAD",
            inst.Name,
            "",
            "",
            "Linked={}".format(linked),
            get_type_name(inst),
        ])

    for inst in FilteredElementCollector(doc).OfClass(RevitLinkInstance).ToElements():
        rvt_rows.append([
            element_id_value(inst.Id),
            "RevitLinkInstance",
            inst.Name,
            "",
            "",
            "",
            get_type_name(inst),
        ])

    return cad_rows, rvt_rows


def write_summary(counts, generated_files):
    lines = []
    lines.append("DIAGNOSTICO BIM - DYNAMO CPYTHON3 - SOMENTE LEITURA")
    lines.append("Documento: {}".format(doc.Title))
    lines.append("Caminho: {}".format(doc.PathName))
    lines.append("")
    lines.append("Garantias:")
    lines.append("- Nao foi criada Transaction.")
    lines.append("- O modelo nao foi alterado.")
    lines.append("- O modelo nao foi salvo.")
    lines.append("- Apenas foram gravados CSVs nesta pasta: {}".format(OUT_DIR))
    lines.append("")
    lines.append("Contagens:")
    for key in [
        "Project Information",
        "Levels",
        "Views",
        "Sheets",
        "Rooms",
        "Areas",
        "Walls",
        "Doors",
        "Windows",
        "Warnings",
        "CAD Links",
        "RVT Links",
    ]:
        lines.append("- {}: {}".format(key, counts.get(key, 0)))
    lines.append("")
    lines.append("Arquivos gerados:")
    for path in generated_files:
        lines.append("- {}".format(path))

    path = os.path.join(OUT_DIR, "00_resumo_diagnostico_bim.txt")
    with open(path, "w", encoding="utf-8-sig") as stream:
        stream.write("\n".join(lines))
    return path


def main():
    ensure_dir(OUT_DIR)

    generated = []

    project_info = collect_project_info()
    levels = collect_levels()
    views = collect_views()
    sheets = collect_sheets()
    rooms = collect_rooms()
    areas = collect_areas()
    walls = collect_walls()
    doors = collect_family_instances(BuiltInCategory.OST_Doors)
    windows = collect_family_instances(BuiltInCategory.OST_Windows)
    warnings = collect_warnings()
    cad_links, rvt_links = collect_links()

    generated.append(write_csv("01_project_information.csv", ["Parametro", "Valor"], project_info))
    generated.append(write_csv("02_levels.csv", ["ElementId", "Nome", "Elevacao_m", "Elevacao_internal_ft"], levels))
    generated.append(write_csv("03_views.csv", ["ElementId", "Nome", "Tipo", "Escala", "IsTemplate", "Categoria"], views))
    generated.append(write_csv("04_sheets.csv", ["ElementId", "Numero", "Nome", "Placeholder"], sheets))
    generated.append(write_csv("05_rooms.csv", ["ElementId", "Numero", "Nome", "Nivel", "Area_m2", "Volume", "Fase"], rooms))
    generated.append(write_csv("06_areas.csv", ["ElementId", "Numero", "Nome", "Nivel", "Area_m2", "Esquema"], areas))
    generated.append(write_csv("07_walls.csv", ["ElementId", "Nome", "Tipo", "NivelBase", "NivelTopo", "Altura", "Comprimento", "FaseCriada", "FaseDemolida"], walls))
    generated.append(write_csv("08_doors.csv", ["ElementId", "Familia", "Tipo", "Nome", "Nivel", "FaseCriada", "FaseDemolida", "Peitoril", "Numero"], doors))
    generated.append(write_csv("09_windows.csv", ["ElementId", "Familia", "Tipo", "Nome", "Nivel", "FaseCriada", "FaseDemolida", "Peitoril", "Numero"], windows))
    generated.append(write_csv("10_warnings.csv", ["Descricao", "Severidade", "ElementosFalha", "ElementosAdicionais"], warnings))
    generated.append(write_csv("11_cad_links.csv", ["ElementId", "TipoReferencia", "Caminho", "CaminhoAbsoluto", "PathType", "Status", "TipoElemento"], cad_links))
    generated.append(write_csv("12_rvt_links.csv", ["ElementId", "TipoReferencia", "Caminho", "CaminhoAbsoluto", "PathType", "Status", "TipoElemento"], rvt_links))

    counts = {
        "Project Information": len(project_info),
        "Levels": len(levels),
        "Views": len(views),
        "Sheets": len(sheets),
        "Rooms": len(rooms),
        "Areas": len(areas),
        "Walls": len(walls),
        "Doors": len(doors),
        "Windows": len(windows),
        "Warnings": len(warnings),
        "CAD Links": len(cad_links),
        "RVT Links": len(rvt_links),
    }

    summary = write_summary(counts, generated)
    generated.insert(0, summary)

    return {
        "status": "OK",
        "out_dir": OUT_DIR,
        "counts": counts,
        "files": generated,
    }


try:
    OUT = main()
except Exception:
    OUT = {
        "status": "ERRO",
        "message": traceback.format_exc(),
        "out_dir": OUT_DIR,
    }
