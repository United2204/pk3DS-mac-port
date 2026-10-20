const $ = id => document.getElementById(id);

let game;
let data;
let original;
let catalog;

const expGroups = ['Medio-rápido', 'Errático', 'Fluctuante', 'Medio-lento', 'Rápido', 'Lento'];
const colors = ['Rojo', 'Azul', 'Amarillo', 'Verde', 'Negro', 'Marrón', 'Morado', 'Gris', 'Blanco', 'Rosa'];
const statLabels = ['PS', 'Ataque', 'Defensa', 'Velocidad', 'At. Esp.', 'Def. Esp.'];
const evLabels = ['PS', 'Ataque', 'Defensa', 'Velocidad', 'At. Esp.', 'Def. Esp.'];

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const result = await response.json();
  if (!response.ok) throw Error(result.error || result.detail || 'Operación fallida');
  return result;
}

function msg(id, text, state = 'neutral') {
  $(id).textContent = text;
  $(id).className = `status ${state}`;
}

function changed() {
  return !!data && JSON.stringify(data) !== JSON.stringify(original);
}

function ui() {
  $('summary').textContent = changed() ? 'Datos modificados' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
}

function numberInput(key, value, min, max, index, step = '1') {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = min;
  input.max = max;
  input.step = step;
  input.value = value ?? 0;
  input.dataset.key = key;
  if (index !== undefined) input.dataset.index = index;
  return input;
}

function readOnlyNumber(value) {
  const input = document.createElement('input');
  input.type = 'number';
  input.value = value ?? 0;
  input.readOnly = true;
  input.tabIndex = -1;
  return input;
}

function selectInput(key, value, options) {
  const input = document.createElement('select');
  input.dataset.key = key;
  let hasValue = false;
  options.forEach(([optionValue, label]) => {
    const option = new Option(label, optionValue);
    option.selected = Number(optionValue) === Number(value);
    hasValue ||= option.selected;
    input.add(option);
  });
  if (!hasValue && value !== null && value !== undefined) {
    input.add(new Option(`Valor ${value}`, value, true, true));
  }
  return input;
}

function checkbox(key, value, index, group) {
  const input = document.createElement('input');
  input.type = 'checkbox';
  input.checked = !!value;
  input.dataset.key = key;
  if (index !== undefined) input.dataset.index = index;
  if (group !== undefined) input.dataset.group = group;
  return input;
}

function field(label, input) {
  const wrapper = document.createElement('label');
  wrapper.className = 'personal-field';
  const caption = document.createElement('span');
  caption.textContent = label;
  wrapper.append(caption, input);
  return wrapper;
}

function arrayField(label, key, values, labels, min, max, step = '1') {
  const wrapper = document.createElement('section');
  wrapper.className = 'personal-array-field';
  const heading = document.createElement('b');
  heading.textContent = label;
  const list = document.createElement('div');
  list.className = 'personal-array';
  (values || []).forEach((value, index) => {
    list.append(field(labels?.[index] || `${index + 1}`, numberInput(key, value, min, max, index, step)));
  });
  wrapper.append(heading, list);
  return wrapper;
}

function panel(title, children) {
  const wrapper = document.createElement('section');
  wrapper.className = 'personal-panel';
  const heading = document.createElement('h2');
  heading.textContent = title;
  const fields = document.createElement('div');
  fields.className = 'personal-fields';
  fields.append(...children.filter(Boolean));
  wrapper.append(heading, fields);
  return wrapper;
}

function gameVersion() {
  return game?.gameVersion || '';
}

function compatibilityCounts() {
  const version = gameVersion();
  if (version === 'XY') return { tmhm: 105, typeTutors: 7, special: [] };
  if (version === 'ORAS' || version === 'ORASDEMO') return { tmhm: 107, typeTutors: 8, special: [15, 17, 16, 15] };
  if (version === 'USUM') return { tmhm: 100, typeTutors: 8, special: [67] };
  if (version === 'SM') return { tmhm: 100, typeTutors: 8, special: [] };
  return { tmhm: 100, typeTutors: 8, special: [] };
}

function bitfield(label, key, bits, count, labelFor, group) {
  if (!Array.isArray(bits) || bits.length === 0) return null;
  const wrapper = document.createElement('section');
  wrapper.className = 'personal-bitfield';
  const heading = document.createElement('b');
  heading.textContent = label;
  const list = document.createElement('div');
  list.className = 'personal-check-grid';
  const visible = Math.min(count, bits.length);
  for (let index = 0; index < visible; index++) {
    const item = document.createElement('label');
    item.className = 'personal-check';
    item.append(checkbox(key, bits[index], index, group));
    item.append(document.createTextNode(labelFor(index)));
    list.append(item);
  }
  wrapper.append(heading, list);
  return wrapper;
}

function compatibilityPanel() {
  const counts = compatibilityCounts();
  const children = [];
  children.push(bitfield(
    'MT / MO',
    'tmhm',
    data.tmhm,
    counts.tmhm,
    index => gameVersion() === 'XY' || gameVersion() === 'ORAS' || gameVersion() === 'ORASDEMO'
      ? index < 100 ? `MT${String(index + 1).padStart(2, '0')}` : `MO${String(index - 99).padStart(2, '0')}`
      : `MT${String(index + 1).padStart(2, '0')}`,
  ));
  children.push(bitfield('Tutores por tipo', 'typeTutors', data.typeTutors, counts.typeTutors, index => `Tutor ${index + 1}`));
  (counts.special || []).forEach((count, group) => {
    const label = gameVersion() === 'ORAS' || gameVersion() === 'ORASDEMO' ? `Tutores OR/AS · grupo ${group + 1}` : 'Tutores de la playa';
    children.push(bitfield(label, 'specialTutors', data.specialTutors?.[group], count, index => `${label} ${index + 1}`, group));
  });
  const note = document.createElement('p');
  note.className = 'personal-note';
  note.textContent = 'Los bits reservados y las compatibilidades sin control visible se conservan al exportar.';
  children.push(note);
  return panel('Compatibilidad de movimientos', children);
}

function normalizeEntry(raw) {
  const value = { ...raw };
  const first = (...names) => names.map(name => raw[name]).find(item => item !== undefined);
  value.evYields = first('evYields', 'eVYields', 'EVYields') || [];
  value.expGrowth = first('expGrowth', 'eXPGrowth', 'EXPGrowth') ?? 0;
  value.baseExp = first('baseExp', 'baseEXP', 'BaseEXP') ?? 0;
  value.tmhm = first('tmhm', 'tMHM', 'TMHM') || [];
  value.typeTutors = first('typeTutors', 'TypeTutors') || [];
  value.specialTutors = first('specialTutors', 'SpecialTutors') || [];
  ['evYields', 'eVYields', 'EVYields', 'expGrowth', 'eXPGrowth', 'EXPGrowth',
    'baseExp', 'baseEXP', 'BaseEXP', 'tmhm', 'tMHM', 'TMHM', 'typeTutors', 'TypeTutors',
    'specialTutors', 'SpecialTutors'].forEach(name => {
    if (name !== 'evYields' && name !== 'expGrowth' && name !== 'baseExp'
      && name !== 'tmhm' && name !== 'typeTutors' && name !== 'specialTutors') delete value[name];
  });
  return value;
}

function render() {
  const fields = $('fields');
  fields.replaceChildren();
  if (!data) {
    ui();
    return;
  }

  fields.append(panel('Datos básicos', [
    arrayField('Estadísticas base', 'stats', data.stats, statLabels, 0, 255),
    arrayField('EV obtenidos', 'evYields', data.evYields, evLabels, 0, 3),
    arrayField('Tipos', 'types', data.types, ['Primario', 'Secundario'], 0, 255),
    field('Ratio de captura', numberInput('catchRate', data.catchRate, 0, 255)),
    field('Etapa evolutiva', readOnlyNumber(data.evoStage)),
    field('Suma de estadísticas (BST)', readOnlyNumber(data.bst)),
    arrayField('Objetos equipados', 'items', data.items, ['50%', '5%', '1%'], 0, 65535),
    field('Ratio de género', numberInput('gender', data.gender, 0, 255)),
    field('Ciclos de eclosión', numberInput('hatchCycles', data.hatchCycles, 0, 255)),
    field('Amistad base', numberInput('baseFriendship', data.baseFriendship, 0, 255)),
    field('Crecimiento de EXP', selectInput('expGrowth', data.expGrowth, expGroups.map((name, index) => [index, `${index} · ${name}`]))),
    arrayField('Grupos huevo', 'eggGroups', data.eggGroups, ['Primario', 'Secundario'], 0, 255),
    arrayField('Habilidades', 'abilities', data.abilities, ['1', '2', 'Oculta'], 0, 255),
    field('Cantidad de formas', readOnlyNumber(data.formeCount)),
    field('Índice de sprite de forma', readOnlyNumber(data.formeSprite)),
    field('Color', selectInput('color', data.color, colors.map((name, index) => [index, `${index} · ${name}`]))),
    field('Color bruto', readOnlyNumber(data.colorRaw)),
    field('EXP base', numberInput('baseExp', data.baseExp, 0, 65535)),
    field('Altura (m)', (() => { const input = numberInput('height', (data.height / 100).toFixed(2), 0, 655.35, undefined, '0.01'); input.dataset.scale = '100'; return input; })()),
    field('Peso (kg)', (() => { const input = numberInput('weight', (data.weight / 10).toFixed(1), 0, 6553.5, undefined, '0.1'); input.dataset.scale = '10'; return input; })()),
  ]));

  if (data.escapeRate !== null && data.escapeRate !== undefined) {
    fields.append(panel('SOS y movimientos Z', [
      field('Ratio de llamada SOS', numberInput('escapeRate', data.escapeRate, 0, 255)),
      field('Objeto Z especial', numberInput('specialZItem', data.specialZItem, 0, 65535)),
      field('Movimiento base Z', numberInput('specialZBaseMove', data.specialZBaseMove, 0, 65535)),
      field('Movimiento Z', numberInput('specialZMove', data.specialZMove, 0, 65535)),
      field('Variante regional', checkbox('localVariant', data.localVariant)),
    ]));
  }

  fields.append(compatibilityPanel());
  ui();
}

function readValue(input) {
  if (input.type === 'checkbox') return input.checked;
  const value = Number(input.value);
  if (!Number.isFinite(value)) return 0;
  return input.dataset.scale ? Math.round(value * Number(input.dataset.scale)) : value;
}

function syncInput(event) {
  const input = event.target;
  const key = input?.dataset?.key;
  if (!key || !data) return;
  const value = readValue(input);
  if (input.dataset.group !== undefined) {
    data[key][Number(input.dataset.group)][Number(input.dataset.index)] = value;
  } else if (input.dataset.index !== undefined) {
    data[key][Number(input.dataset.index)] = value;
  } else {
    data[key] = value;
  }
  ui();
}

async function load() {
  try {
    const [inspect, speciesCatalog] = await Promise.all([
      post('/api/workspace/inspect', { workspacePath: $('workspace').value }),
      post('/api/editors/levelup/catalog', { workspacePath: $('workspace').value, language: 2 }),
    ]);
    game = inspect;
    catalog = speciesCatalog;
    const species = $('species');
    species.replaceChildren();
    for (const item of catalog.species) species.add(new Option(`${item.id} · ${item.name}`, item.id));
    species.disabled = false;
    $('open').disabled = false;
    $('editor').classList.remove('is-disabled');
    msg('status', `${inspect.gameVersion} cargado.`, inspect.titleId ? 'success' : 'error');
  } catch (error) {
    msg('status', error.message, 'error');
  }
  ui();
}

async function open() {
  try {
    const raw = await post('/api/editors/personal/entry', {
      workspacePath: $('workspace').value,
      speciesIndex: Number($('species').value),
      language: 2,
    });
    data = normalizeEntry(raw);
    original = structuredClone(data);
    render();
    msg('result', `Entrada ${data.speciesIndex} abierta.`);
  } catch (error) {
    msg('result', error.message, 'error');
  }
}

$('browse').onclick = async () => {
  try {
    $('workspace').value = (await post('/api/workspace/pick')).path;
    load();
  } catch (error) {
    msg('status', error.message, 'error');
  }
};

$('load').onclick = load;
$('open').onclick = open;
$('fields').oninput = syncInput;
$('fields').onchange = syncInput;

$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const { evoStage, formeCount, formeSprite, bst, colorRaw, ...editable } = data;
    const result = await post('/api/editors/personal/export', {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      speciesIndex: Number($('species').value),
      ...editable,
      language: 2,
    });
    original = structuredClone(data);
    msg('result', `Listo. ZIP: ${result.zipPath}`, 'success');
    ui();
  } catch (error) {
    msg('result', error.message, 'error');
  }
};

ui();
