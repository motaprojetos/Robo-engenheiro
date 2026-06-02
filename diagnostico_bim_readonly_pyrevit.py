# -*- coding: utf-8 -*-
"""
pyRevit - Diagnostico BIM somente leitura

Executar com o modelo aberto no Revit.
Nao cria Transaction, nao altera elementos e nao salva o modelo.

Saidas:
- CSVs detalhados por categoria
- TXT resumido
"""

from __future__ import print_function

import codecs
import os
import traceback

from Autodesk.Revit.DB import (
    BuiltInCategory,
    BuiltInParameter,
    ElementId,
    ExternalFileReferenceType,
    FilteredElementCollector,
    ImportInstance,
    Level,
    ModelPathUtils,
    RevitLinkInstance,
    StorageType,
    TransmissionData,
    UnitUtils,
    UnitTypeId,
    View,
    ViewSheet,
    Wall,
)
from Autodesk.Revit.DB.Architecture import Room
from pyrevit import revit, script


doc = revit.doc
output = script.get_output()

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
        text = unicode(value)
    except NameError:
        text = str(value)
    except Exception:
        text = str(value)
    return text.replace("\r", " ").replace("\n", " ").strip()


def element_id_value(element_id):
    if element_id is None:
        return ""
    try:
        return element_id.IntegerValue
    except Exception:
        return safe_text(element_id)


def param_as_text(element, param_name):
    if element is None:
        return ""
    try:
        param = element.LookupParameter(param_name)
    except Exception:
        param = None
    if not param:
        return ""
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
    try:
        return safe_text(param.AsValueString())
    except Exception:
        return ""


def builtin_param_as_text(element, builtin_param):
    if builtin_param is None:
        return ""
    try:
        param = element.get_Parameter(builtin_param)
        if param:
            value = param.AsValueString()
            if value:
                return safe_text(value)
            return param_as_text_from_param(param)
    except Exception:
        pass
    return ""


def get_builtin_parameter(name):
    try:
        return getattr(BuiltInParameter, name)
    except Exception:
        return None


def param_as_text_from_param(param):
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
    try:
        return safe_text(param.AsValueString())
    except Exception:
        return ""


def feet_to_m(value):
    try:
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Meters)
    except Exception:
        return value * 0.3048


def sqfeet_to_sqm(value):
    try:
        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.SquareMeters)
    except Exception:
        return value * 0.09290304


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


def get_category_name(element):
    try:
        if element.Category:
            return safe_text(element.Category.Name)
    except Exception:
        pass
    return ""


def write_csv(filename, headers, rows):
    path = os.path.join(OUT_DIR, filename)
    with codecs.open(path, "w", "utf-8-sig") as stream:
        stream.write(",".join([csv_cell(value) for value in headers]) + "\n")
        for row in rows:
            stream.write(",".join([csv_cell(value) for value in row]) + "\n")
    return path


def csv_cell(value):
    text = safe_text(value)
    text = text.replace('"', '""')
    return '"' + text + '"'


def write_txt(filename, text):
    path = os.path.join(OUT_DIR, filename)
    with codecs.open(path, "w", "utf-8-sig") as stream:
        stream.write(text)
    return path


def collect_project_information():
    info = doc.ProjectInformation
    rows = []
    if info:
        rows.append(["ElementId", element_id_value(info.Id)])
        rows.append(["Name", safe_text(info.Name)])
        rows.append(["Number", safe_text(info.Number)])
        rows.append(["ClientName", safe_text(info.ClientName)])
        rows.append(["ProjectName", safe_text(info.Name)])
        rows.append(["ProjectNumber", safe_text(info.Number)])
        rows.append(["Address", safe_text(info.Address)])
        rows.append(["Status", safe_text(info.Status)])
        rows.append(["IssueDate", safe_text(info.IssueDate)])
        for param in info.Parameters:
            try:
                rows.append([
                    safe_text(param.Definition.Name),
                    param_as_text_from_param(param),
                ])
            except Exception:
                pass
    return rows


def collect_levels():
    rows = []
    levels = FilteredElementCollector(doc).OfClass(Level).ToElements()
    for level in sorted(levels, key=lambda item: item.Elevation):
        rows.append([
            element_id_value(level.Id),
            level.Name,
            "{0:.3f}".format(feet_to_m(level.Elevation)),
            level.Elevation,
        ])
    return rows


def collect_views():
    rows = []
    views = FilteredElementCollector(doc).OfClass(View).ToElements()
    for view in sorted(views, key=lambda item: safe_text(item.Name)):
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
            get_category_name(view),
        ])
    return rows


def collect_sheets():
    rows = []
    sheets = FilteredElementCollector(doc).OfClass(ViewSheet).ToElements()
    for sheet in sorted(sheets, key=lambda item: safe_text(item.SheetNumber)):
        rows.append([
            element_id_value(sheet.Id),
            sheet.SheetNumber,
            sheet.Name,
            sheet.IsPlaceholder,
        ])
    return rows


def collect_rooms():
    rows = []
    rooms = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElements()
    for room in sorted(rooms, key=lambda item: (safe_text(param_as_text(item, "Número")), safe_text(item.Name))):
        area = 0.0
        try:
            area = room.Area
        except Exception:
            pass
        rows.append([
            element_id_value(room.Id),
            param_as_text(room, "Número") or param_as_text(room, "Number"),
            room.Name,
            safe_text(room.Level.Name) if getattr(room, "Level", None) else "",
            "{0:.2f}".format(sqfeet_to_sqm(area)) if area else "",
            param_as_text(room, "Volume"),
            param_as_text(room, "Fase") or param_as_text(room, "Phase"),
        ])
    return rows


def collect_areas():
    rows = []
    areas = FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().ToElements()
    for area in sorted(areas, key=lambda item: safe_text(item.Name)):
        area_value = 0.0
        try:
            area_value = area.Area
        except Exception:
            pass
        rows.append([
            element_id_value(area.Id),
            param_as_text(area, "Número") or param_as_text(area, "Number"),
            area.Name,
            safe_text(area.Level.Name) if getattr(area, "Level", None) else "",
            "{0:.2f}".format(sqfeet_to_sqm(area_value)) if area_value else "",
            param_as_text(area, "Esquema de área") or param_as_text(area, "Area Scheme"),
        ])
    return rows


def collect_walls():
    rows = []
    walls = FilteredElementCollector(doc).OfClass(Wall).WhereElementIsNotElementType().ToElements()
    for wall in walls:
        rows.append([
            element_id_value(wall.Id),
            safe_text(wall.Name),
            get_type_name(wall),
            builtin_param_as_text(wall, BuiltInParameter.WALL_BASE_CONSTRAINT),
            builtin_param_as_text(wall, BuiltInParameter.WALL_HEIGHT_TYPE),
            builtin_param_as_text(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM),
            builtin_param_as_text(wall, BuiltInParameter.CURVE_ELEM_LENGTH),
            builtin_param_as_text(wall, BuiltInParameter.PHASE_CREATED),
            builtin_param_as_text(wall, BuiltInParameter.PHASE_DEMOLISHED),
        ])
    return rows


def collect_family_instances(category):
    rows = []
    elems = FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType().ToElements()
    for elem in elems:
        family = ""
        symbol = ""
        try:
            symbol = elem.Symbol
            family = safe_text(symbol.Family.Name)
            symbol = safe_text(symbol.Name)
        except Exception:
            pass
        rows.append([
            element_id_value(elem.Id),
            family,
            symbol,
            safe_text(elem.Name),
            builtin_param_as_text(elem, BuiltInParameter.FAMILY_LEVEL_PARAM),
            builtin_param_as_text(elem, BuiltInParameter.PHASE_CREATED),
            builtin_param_as_text(elem, BuiltInParameter.PHASE_DEMOLISHED),
            builtin_param_as_text(elem, get_builtin_parameter("INSTANCE_SILL_HEIGHT_PARAM")),
            builtin_param_as_text(elem, get_builtin_parameter("DOOR_NUMBER")),
        ])
    return rows


def collect_warnings():
    rows = []
    try:
        warnings = doc.GetWarnings()
    except Exception:
        warnings = []
    for warning in warnings:
        try:
            failing_ids = [element_id_value(x) for x in warning.GetFailingElements()]
        except Exception:
            failing_ids = []
        try:
            additional_ids = [element_id_value(x) for x in warning.GetAdditionalElements()]
        except Exception:
            additional_ids = []
        rows.append([
            safe_text(warning.GetDescriptionText()),
            safe_text(warning.GetSeverity()),
            ";".join([safe_text(x) for x in failing_ids]),
            ";".join([safe_text(x) for x in additional_ids]),
        ])
    return rows


def external_ref_to_row(ref_id, ref):
    try:
        ext_ref = ref.GetExternalFileReference()
    except Exception:
        ext_ref = ref
    if not ext_ref:
        return [element_id_value(ref_id), "", "", "", "", "", ""]
    try:
        ref_type = safe_text(ext_ref.ExternalFileReferenceType)
    except Exception:
        ref_type = ""
    try:
        path = safe_text(ModelPathUtils.ConvertModelPathToUserVisiblePath(ext_ref.GetPath()))
    except Exception:
        try:
            path = safe_text(ext_ref.GetPath())
        except Exception:
            path = ""
    try:
        absolute_path = safe_text(ModelPathUtils.ConvertModelPathToUserVisiblePath(ext_ref.GetAbsolutePath()))
    except Exception:
        try:
            absolute_path = safe_text(ext_ref.GetAbsolutePath())
        except Exception:
            absolute_path = ""
    try:
        load_status = safe_text(ext_ref.GetLinkedFileStatus())
    except Exception:
        load_status = ""
    try:
        path_type = safe_text(ext_ref.PathType)
    except Exception:
        path_type = ""
    return [
        element_id_value(ref_id),
        ref_type,
        path,
        absolute_path,
        path_type,
        load_status,
        "",
    ]


def collect_external_references():
    cad_rows = []
    rvt_rows = []

    try:
        transmission_data = None
        if doc.PathName:
            model_path = ModelPathUtils.ConvertUserVisiblePathToModelPath(doc.PathName)
            transmission_data = TransmissionData.ReadTransmissionData(model_path)
    except Exception:
        transmission_data = None

    if transmission_data:
        for ref_id in transmission_data.GetAllExternalFileReferenceIds():
            ref = transmission_data.GetLastSavedReferenceData(ref_id)
            row = external_ref_to_row(ref_id, ref)
            if "CAD" in row[1]:
                cad_rows.append(row)
            elif "Revit" in row[1]:
                rvt_rows.append(row)

    # Fallback/confirmacao por elementos presentes no documento aberto.
    for inst in FilteredElementCollector(doc).OfClass(ImportInstance).ToElements():
        try:
            is_linked = inst.IsLinked
        except Exception:
            is_linked = ""
        cad_rows.append([
            element_id_value(inst.Id),
            "ImportInstance/CAD",
            safe_text(inst.Name),
            "",
            "",
            "Linked={0}".format(is_linked),
            get_type_name(inst),
        ])

    for inst in FilteredElementCollector(doc).OfClass(RevitLinkInstance).ToElements():
        rvt_rows.append([
            element_id_value(inst.Id),
            "RevitLinkInstance",
            safe_text(inst.Name),
            "",
            "",
            "",
            get_type_name(inst),
        ])

    return cad_rows, rvt_rows


def main():
    ensure_dir(OUT_DIR)

    generated = []

    project_rows = collect_project_information()
    levels = collect_levels()
    views = collect_views()
    sheets = collect_sheets()
    rooms = collect_rooms()
    areas = collect_areas()
    walls = collect_walls()
    doors = collect_family_instances(BuiltInCategory.OST_Doors)
    windows = collect_family_instances(BuiltInCategory.OST_Windows)
    warnings = collect_warnings()
    cad_links, rvt_links = collect_external_references()

    generated.append(write_csv("01_project_information.csv", ["Parametro", "Valor"], project_rows))
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

    summary = []
    summary.append("DIAGNOSTICO BIM - SOMENTE LEITURA")
    summary.append("Documento: {0}".format(doc.Title))
    summary.append("Caminho: {0}".format(doc.PathName))
    summary.append("")
    summary.append("Garantias:")
    summary.append("- Nenhuma Transaction foi criada.")
    summary.append("- O modelo nao foi salvo.")
    summary.append("- O script apenas le dados e grava CSV/TXT na pasta de diagnostico.")
    summary.append("")
    summary.append("Contagens:")
    summary.append("- Project Information: {0} parametros/linhas".format(len(project_rows)))
    summary.append("- Levels: {0}".format(len(levels)))
    summary.append("- Views: {0}".format(len(views)))
    summary.append("- Sheets: {0}".format(len(sheets)))
    summary.append("- Rooms: {0}".format(len(rooms)))
    summary.append("- Areas: {0}".format(len(areas)))
    summary.append("- Walls: {0}".format(len(walls)))
    summary.append("- Doors: {0}".format(len(doors)))
    summary.append("- Windows: {0}".format(len(windows)))
    summary.append("- Warnings: {0}".format(len(warnings)))
    summary.append("- CAD Links / Imports: {0}".format(len(cad_links)))
    summary.append("- RVT Links: {0}".format(len(rvt_links)))
    summary.append("")
    summary.append("Arquivos gerados:")
    for path in generated:
        summary.append("- {0}".format(path))

    summary_path = write_txt("00_resumo_diagnostico_bim.txt", "\n".join(summary))
    generated.insert(0, summary_path)

    output.print_md("## Diagnostico BIM concluido")
    output.print_md("Pasta de saida: `{0}`".format(OUT_DIR))
    output.print_md("Arquivos gerados: {0}".format(len(generated)))
    for path in generated:
        output.print_md("- `{0}`".format(path))


try:
    main()
except Exception:
    output.print_md("## Erro no diagnostico BIM")
    output.print_md("O modelo nao foi alterado. Detalhes:")
    output.print_md("```")
    output.print_md(traceback.format_exc())
    output.print_md("```")
