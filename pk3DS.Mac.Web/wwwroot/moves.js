const $ = id => document.getElementById(id);

let game;
let data;
let original;
let moveCatalog = [];

const TYPE_NAMES = [
    "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
    "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy",
];
const CATEGORY_NAMES = ["Status", "Physical", "Special"];
const QUALITY_NAMES = [
    "Only DMG", "No DMG → Inflict Status", "No DMG → −Target/+User Stat", "No DMG | Heal User",
    "DMG | Inflict Status", "No DMG | STATUS | +Target Stat", "DMG | −Target Stat", "DMG | +User Stat",
    "DMG | Absorbs DMG", "One-Hit KO", "Affects Whole Field", "Affect One Side of the Field",
    "Forces Target to Switch", "Unique Effect",
];
const INFLICT_NAMES_6 = [
    "None", "Paralyze", "Sleep", "Freeze", "Burn", "Poison", "Confusion", "Attract", "Capture", "Nightmare",
    "Curse", "Taunt", "Torment", "Disable", "Yawn", "Heal Block", "?", "Detect", "Leech Seed", "Embargo",
    "Perish Song", "Ingrain",
];
const INFLICT_NAMES_7 = [
    ...INFLICT_NAMES_6, "??? 0x16", "??? 0x17", "Mute",
];
const TARGET_NAMES = [
    "Single Adjacent Ally/Foe", "Any Ally", "Any Adjacent Ally", "Single Adjacent Foe",
    "Everyone but User", "All Foes", "All Allies", "Self", "All Pokémon on Field",
    "Single Adjacent Foe (2)", "Entire Field", "Opponent's Field", "User's Field", "Self",
];
const STAT_NAMES = ["None", "Attack", "Defense", "Special Attack", "Special Defense", "Speed", "Accuracy", "Evasion", "All"];
const REFRESH_NAMES = ["None", "Disheveled", "Mud", "Dust", "Dry"];
const Z_EFFECT_NAMES = [
    "None", "+1 Attack", "+2 Attack", "+3 Attack", "+1 Defense", "+2 Defense", "+3 Defense",
    "+1 Special Attack", "+2 Special Attack", "+3 Special Attack", "+1 Special Defense", "+2 Special Defense",
    "+3 Special Defense", "+1 Speed", "+2 Speed", "+3 Speed", "+1 Accuracy", "+2 Accuracy", "+3 Accuracy",
    "+1 Evasiveness", "+2 Evasiveness", "+3 Evasiveness", "+1 to all (except Accuracy or Evasiveness)",
    "+2 to all (except Accuracy or Evasiveness)", "+3 to all (except Accuracy or Evasiveness)",
    "Raises critical-hit ratio two stages", "Resets lowered stats of the user", "Recovers all of user's HP",
    "Recovers all HP of the Pokémon switching-in", "Makes the user the center of attention",
    "Curse: full HP as Ghost, +1 Attack otherwise",
];
const FLAG_NAMES_6 = [
    "MakesContact", "Charge", "Recharge", "Protect", "Reflectable", "Snatch", "Mirror", "Punch",
    "Sound", "Gravity", "Defrost", "DistanceTriple", "Heal", "IgnoreSubstitute", "FailSkyBattle", "AnimateAlly",
    "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24", "F25", "F26", "F27", "F28", "F29", "F30", "F31", "F32",
];
const FLAG_NAMES_7 = [
    "MakesContact", "Charge", "Recharge", "Protect", "Reflectable", "Snatch", "Mirror", "Punch",
    "Sound", "Gravity", "Defrost", "DistanceTriple", "Heal", "IgnoreSubstitute", "FailSkyBattle", "AnimateAlly",
    "Dance", "F18", "F19", "F20", "F21", "F22", "F23", "F24", "F25", "F26", "F27", "F28", "F29", "F30", "F31", "F32",
];

async function post(url, body = {}) {
    const response = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
    });
    const result = await response.json();
    if (!response.ok)
        throw Error(result.error || result.detail || "Operación fallida");
    return result;
}

function message(id, text, state = "neutral") {
    $(id).textContent = text;
    $(id).className = `status ${state}`;
}

function changed() {
    return JSON.stringify(data) !== JSON.stringify(original);
}

function updateState() {
    const dirty = changed();
    $("summary").textContent = dirty ? "Movimiento modificado" : "Sin cambios para exportar";
    $("export").disabled = !game?.titleId || !dirty;
}

function addOptions(select, options, value, values = null) {
    options.forEach((label, index) => {
        const option = document.createElement("option");
        const optionValue = values?.[index] ?? index;
        option.value = optionValue;
        option.textContent = `${optionValue} · ${label}`;
        select.append(option);
    });

    const current = value == null ? "" : String(value);
    if (current && !options.some((_, index) => String(values?.[index] ?? index) === current)) {
        const option = document.createElement("option");
        option.value = current;
        option.textContent = `${current} · Valor sin etiqueta`;
        select.append(option);
    }
    select.value = current;
}

function addMoveOptions(select, value) {
    moveCatalog.forEach(move => {
        const option = document.createElement("option");
        option.value = move.id;
        option.textContent = `${move.id} · ${move.name}`;
        select.append(option);
    });
    const current = value == null ? "" : String(value);
    if (current && !moveCatalog.some(move => String(move.id) === current)) {
        const option = document.createElement("option");
        option.value = current;
        option.textContent = `${current} · Movimiento sin nombre`;
        select.append(option);
    }
    select.value = current;
}

function addField(grid, key, label, options = {}) {
    const field = document.createElement("label");
    field.className = "move-field";
    field.textContent = label;

    const control = document.createElement(options.choices || options.moves ? "select" : "input");
    control.dataset.k = key;
    if (options.choices)
        addOptions(control, options.choices, data[key], options.values);
    else if (options.moves)
        addMoveOptions(control, data[key]);
    else {
        control.type = "number";
        if (options.min != null) control.min = options.min;
        if (options.max != null) control.max = options.max;
        control.value = data[key] ?? "";
    }
    if (options.title) control.title = options.title;
    field.append(control);
    grid.append(field);
}

function addSection(parent, title, fields, className = "") {
    const section = document.createElement("section");
    if (className) section.className = className;
    const heading = document.createElement("h2");
    heading.textContent = title;
    section.append(heading);
    const grid = document.createElement("div");
    grid.className = "move-grid";
    fields.forEach(([key, label, options]) => addField(grid, key, label, options));
    section.append(grid);
    parent.append(section);
}

function addFlags(parent) {
    const section = document.createElement("section");
    section.className = "move-wide";
    const heading = document.createElement("h2");
    heading.textContent = "Flags";
    section.append(heading);
    const grid = document.createElement("div");
    grid.className = "move-flags";
    const names = data.generation === 7 ? FLAG_NAMES_7 : FLAG_NAMES_6;
    const flags = Number(data.flags ?? 0) >>> 0;
    names.forEach((name, index) => {
        const label = document.createElement("label");
        label.className = "move-flag";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.dataset.flag = index;
        checkbox.checked = ((flags >>> index) & 1) === 1;
        const text = document.createElement("span");
        text.textContent = `${index} · ${name}`;
        label.append(checkbox, text);
        grid.append(label);
    });
    section.append(grid);
    parent.append(section);
}

function render() {
    const fields = $("fields");
    fields.replaceChildren();
    const inflictNames = [...(data.generation === 7 ? INFLICT_NAMES_7 : INFLICT_NAMES_6), "Special"];
    const inflictValues = [...Array(inflictNames.length - 1).keys(), -1];

    addSection(fields, "Valores básicos", [
        ["type", "Tipo", { choices: TYPE_NAMES }],
        ["quality", "Calidad", { choices: QUALITY_NAMES }],
        ["category", "Categoría", { choices: CATEGORY_NAMES }],
        ["power", "Potencia", { min: 0, max: 255 }],
        ["accuracy", "Precisión", { min: 0, max: 255 }],
        ["pp", "PP", { min: 0, max: 255 }],
        ["priority", "Prioridad", { min: -128, max: 127 }],
    ]);

    addSection(fields, "Efectos y duración", [
        ["hitMin", "Mínimo de golpes", { min: 0, max: 15 }],
        ["hitMax", "Máximo de golpes", { min: 0, max: 15 }],
        ["inflict", "Estado / efecto", {
            choices: inflictNames,
            values: inflictValues,
            title: "El valor Especial de Windows usa -1; los IDs desconocidos se conservan.",
        }],
        ["inflictPercent", "Probabilidad de estado", { min: 0, max: 255 }],
        ["inflictCount", "Parámetro 0x0B", {
            min: 0,
            max: 255,
            title: "Windows lo relaciona con «skip immunity», sin describir su significado; se conserva el valor original.",
        }],
        ["turnMin", "Mínimo de turnos", { min: 0, max: 255 }],
        ["turnMax", "Máximo de turnos", { min: 0, max: 255 }],
        ["critStage", "Nivel crítico", { min: 0, max: 255 }],
        ["flinch", "Probabilidad de retroceso", { min: 0, max: 255 }],
        ["effectSequence", "Secuencia de efecto", { min: 0, max: 65535 }],
        ["recoil", "Retroceso / absorción", { min: -128, max: 127 }],
        ["healing", "Curación", { min: 0, max: 255 }],
    ]);

    addSection(fields, "Objetivo y cambios de estadísticas", [
        ["target", "Objetivo", { choices: TARGET_NAMES }],
        ["stat1", "Estadística 1", { choices: STAT_NAMES }],
        ["stat1Stage", "Cambio de nivel 1", { min: -128, max: 127 }],
        ["stat1Percent", "Probabilidad 1", { min: 0, max: 255 }],
        ["stat2", "Estadística 2", { choices: STAT_NAMES }],
        ["stat2Stage", "Cambio de nivel 2", { min: -128, max: 127 }],
        ["stat2Percent", "Probabilidad 2", { min: 0, max: 255 }],
        ["stat3", "Estadística 3", { choices: STAT_NAMES }],
        ["stat3Stage", "Cambio de nivel 3", { min: -128, max: 127 }],
        ["stat3Percent", "Probabilidad 3", { min: 0, max: 255 }],
    ]);

    addFlags(fields);

    if (data.generation === 7) {
        addSection(fields, "Movimientos Z y refresh", [
            ["zMove", "Movimiento Z", { moves: true }],
            ["zPower", "Potencia Z", { min: 0, max: 255 }],
            ["zEffect", "Efecto Z", { choices: Z_EFFECT_NAMES }],
            ["refreshAfflictType", "Refresh: tipo", { choices: REFRESH_NAMES }],
            ["refreshAfflictPercent", "Refresh: probabilidad", { min: 0, max: 255 }],
        ]);
    }
    updateState();
}

function editField(target) {
    if (target.dataset.k) {
        const value = Number(target.value);
        if (Number.isFinite(value))
            data[target.dataset.k] = value;
    }
    if (target.dataset.flag != null) {
        let flags = Number(data.flags ?? 0) >>> 0;
        const bit = Number(target.dataset.flag);
        if (target.checked)
            flags = (flags | (1 << bit)) >>> 0;
        else
            flags = (flags & ~(1 << bit)) >>> 0;
        data.flags = flags;
    }
    updateState();
}

async function load() {
    try {
        const [loadedGame, catalog] = await Promise.all([
            post("/api/workspace/inspect", { workspacePath: $("workspace").value }),
            post("/api/editors/levelup/catalog", { workspacePath: $("workspace").value, language: 2 }),
        ]);
        game = loadedGame;
        moveCatalog = catalog.moves ?? [];
        const move = $("move");
        move.replaceChildren();
        moveCatalog.forEach(entry => {
            const option = document.createElement("option");
            option.value = entry.id;
            option.textContent = `${entry.id} · ${entry.name}`;
            move.append(option);
        });
        move.disabled = false;
        $("open").disabled = false;
        $("editor").classList.remove("is-disabled");
        message("status", `${loadedGame.gameVersion} cargado.`, loadedGame.titleId ? "success" : "warning");
    } catch (error) {
        message("status", error.message, "error");
    }
    updateState();
}

async function open() {
    try {
        data = await post("/api/editors/moves/entry", {
            workspacePath: $("workspace").value,
            moveIndex: Number($("move").value),
            language: 2,
        });
        original = structuredClone(data);
        render();
        message("result", `Movimiento ${data.moveIndex} abierto.`, "success");
    } catch (error) {
        message("result", error.message, "error");
    }
}

$("browse").onclick = async () => {
    try {
        $("workspace").value = (await post("/api/workspace/pick")).path;
        await load();
    } catch (error) {
        message("status", error.message, "error");
    }
};
$("load").onclick = load;
$("open").onclick = open;
$("fields").oninput = event => editField(event.target);
$("fields").onchange = event => editField(event.target);
$("export").onclick = async () => {
    try {
        const output = await post("/api/workspace/pick-output");
        const request = {
            workspacePath: $("workspace").value,
            outputDirectory: output.path,
            titleId: game.titleId,
            moveIndex: Number($("move").value),
            ...data,
            language: 2,
        };
        delete request.moveIndex;
        delete request.generation;
        const result = await post("/api/editors/moves/export", {
            ...request,
            moveIndex: Number($("move").value),
        });
        original = structuredClone(data);
        message("result", `Listo. ZIP: ${result.zipPath}`, "success");
        updateState();
    } catch (error) {
        message("result", error.message, "error");
    }
};
updateState();
