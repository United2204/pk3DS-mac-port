const $ = id => document.getElementById(id);
let game;
let catalog;
let data;
let original;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const value = await response.json();
  if (!response.ok)
    throw Error(value.error || value.detail || 'Operación fallida');
  return value;
}

function msg(id, text, state = 'neutral') {
  $(id).textContent = text;
  $(id).className = `status ${state}`;
}

function changed() {
  return !!data && JSON.stringify(data) !== JSON.stringify(original);
}

function seedValue() {
  const raw = $('rand-seed').value.trim();
  if (raw === '')
    return null;
  if (!/^-?\d+$/.test(raw))
    throw Error('La semilla debe ser un entero con signo de 32 bits.');
  const value = Number(raw);
  if (!Number.isSafeInteger(value) || value < -2147483648 || value > 2147483647)
    throw Error('La semilla debe ser un entero con signo de 32 bits.');
  return value;
}

function randomizerIsCompatible() {
  return !!catalog && !!game && ['XY', 'ORAS'].includes(game.gameVersion);
}

function randomizerValuesValid() {
  const multiplier = Number($('rand-level-multiplier').value);
  const threshold = Number($('rand-evolution-level').value);
  const rawSeed = $('rand-seed').value.trim();
  const validSeed = rawSeed === '' || (/^-?\d+$/.test(rawSeed)
    && Number.isSafeInteger(Number(rawSeed))
    && Number(rawSeed) >= -2147483648
    && Number(rawSeed) <= 2147483647);
  return Number.isFinite(multiplier) && multiplier >= 0 && multiplier <= 3
    && Number.isInteger(threshold) && threshold >= 1 && threshold <= 100
    && validSeed;
}

function updateRandomizerUi() {
  const panel = $('randomizer');
  const button = $('randomize-run');
  const compatible = randomizerIsCompatible();
  panel.hidden = !compatible;
  panel.classList.toggle('is-disabled', !compatible || !game?.titleId);
  button.disabled = !compatible || !game?.titleId || !randomizerValuesValid();
}

function ui() {
  $('summary').textContent = changed() ? 'Regalo modificado' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed();
  updateRandomizerUi();
}

function select(items, value, key) {
  const input = document.createElement('select');
  input.dataset.key = key;
  for (const item of items) {
    const option = new Option(`${item.id} · ${item.name}`, item.id);
    option.selected = item.id === value;
    input.add(option);
  }
  return input;
}

function options(items, value, key) {
  const input = document.createElement('select');
  input.dataset.key = key;
  for (const [id, name] of items) {
    const option = new Option(name, id);
    option.value = id;
    option.selected = id === value;
    input.add(option);
  }
  return input;
}

function numberInput(key, value, min, max, index) {
  const input = document.createElement('input');
  input.type = 'number';
  input.min = min;
  input.max = max;
  input.value = value;
  input.dataset.key = key;
  if (index !== undefined)
    input.dataset.index = index;
  return input;
}

function checkbox(key, value) {
  const label = document.createElement('label');
  label.className = 'gift-check';
  const input = document.createElement('input');
  input.type = 'checkbox';
  input.checked = !!value;
  input.dataset.key = key;
  label.append(input, 'Activo');
  return label;
}

function field(label, input) {
  const section = document.createElement('section');
  const title = document.createElement('b');
  title.textContent = label;
  section.append(title, input);
  return section;
}

function ivField() {
  const section = document.createElement('section');
  const title = document.createElement('b');
  title.textContent = 'IVs (-1 = aleatorio)';
  const grid = document.createElement('div');
  grid.className = 'gift-ivs';
  ['PS', 'Ataque', 'Defensa', 'At. Esp.', 'Def. Esp.', 'Velocidad'].forEach((name, index) => {
    const label = document.createElement('label');
    label.append(name, numberInput('ivs', data.ivs[index], -1, 31, index));
    grid.append(label);
  });
  section.append(title, grid);
  return section;
}

function render() {
  const box = $('fields');
  box.replaceChildren();
  if (!data)
    return;
  box.append(
    field('Especie', select(catalog.species, data.species, 'species')),
    field('Objeto equipado', options([[-1, 'Ninguno'], ...catalog.items.map(item => [item.id, item.name])], data.heldItem, 'heldItem')),
    field('Nivel', numberInput('level', data.level, 0, 100)),
    field('Forma', numberInput('form', data.form, 0, 255)),
    field('Género', options([[0, 'Aleatorio / sin fijar'], [1, 'Macho'], [2, 'Hembra']], data.gender, 'gender')),
    field('Habilidad', options([[-1, 'Aleatoria'], [0, 'Habilidad 1'], [1, 'Habilidad 2'], [2, 'Habilidad oculta']], data.ability, 'ability')),
    field('Naturaleza', options([[-1, 'Aleatoria'], ...catalog.natures.map(nature => [nature.id, nature.name])], data.nature, 'nature')),
    field('Bloqueo shiny', checkbox('shinyLock', data.shinyLock)),
    ivField(),
  );
  ui();
}

function update(target) {
  const key = target.dataset.key;
  if (!key || !data)
    return;
  if (target.dataset.index !== undefined)
    data[key][+target.dataset.index] = +target.value;
  else if (target.type === 'checkbox')
    data[key] = target.checked;
  else
    data[key] = +target.value;
  ui();
}

async function load() {
  try {
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    catalog = await post('/api/editors/gift/gen6/catalog', { workspacePath: $('workspace').value, language: 2 });
    const entries = $('entry');
    entries.replaceChildren();
    for (let index = 0; index < catalog.count; index++)
      entries.add(new Option(`Entrada ${String(index).padStart(2, '0')}`, index));
    entries.disabled = false;
    $('open').disabled = false;
    $('editor').classList.remove('is-disabled');
    msg('status', game.titleId ? `${game.gameVersion} cargado.` : `${game.gameVersion} cargado; falta detectar el Title ID.`, game.titleId ? 'success' : 'neutral');
    updateRandomizerUi();
  } catch (error) {
    game = undefined;
    catalog = undefined;
    data = undefined;
    original = undefined;
    $('entry').replaceChildren();
    $('entry').disabled = true;
    $('open').disabled = true;
    $('editor').classList.add('is-disabled');
    $('fields').replaceChildren();
    msg('status', error.message, 'error');
  }
  ui();
}

async function open() {
  try {
    const response = await post('/api/editors/gift/gen6/entry', {
      workspacePath: $('workspace').value,
      entryIndex: +$('entry').value,
      language: 2,
    });
    const value = response.entry;
    data = { ...value, ivs: value.iVs ?? value.ivs };
    delete data.iVs;
    original = structuredClone(data);
    render();
    msg('result', `Entrada ${response.entryIndex} abierta.`, 'success');
  } catch (error) {
    msg('result', error.message, 'error');
  }
}

function randomizeRequest(outputDirectory) {
  const multiplier = Number($('rand-level-multiplier').value);
  const threshold = Number($('rand-evolution-level').value);
  if (!Number.isFinite(multiplier) || multiplier < 0 || multiplier > 3)
    throw Error('El multiplicador de nivel debe estar entre 0.00 y 3.00.');
  if (!Number.isInteger(threshold) || threshold < 1 || threshold > 100)
    throw Error('El nivel para forzar evoluciones debe estar entre 1 y 100.');
  return {
    workspacePath: $('workspace').value,
    outputDirectory,
    titleId: game.titleId,
    language: 2,
    allowGen1: $('rand-g1').checked,
    allowGen2: $('rand-g2').checked,
    allowGen3: $('rand-g3').checked,
    allowGen4: $('rand-g4').checked,
    allowGen5: $('rand-g5').checked,
    allowGen6: $('rand-g6').checked,
    includeLegendary: $('rand-legendary').checked,
    includeMythical: $('rand-mythical').checked,
    matchBst: $('rand-bst').checked,
    replaceMegaGifts: $('rand-replace-mega').checked,
    allowMegaForms: $('rand-mega-forms').checked,
    randomizeHeldItems: $('rand-items').checked,
    randomizeLevels: $('rand-levels').checked,
    levelMultiplier: multiplier,
    removeShinyLocks: $('rand-shiny').checked,
    randomizeAbilities: $('rand-abilities').checked,
    forceFullyEvolved: $('rand-evolution').checked,
    fullyEvolvedLevel: threshold,
    seed: seedValue(),
  };
}

async function randomizeAll() {
  try {
    const output = await post('/api/workspace/pick-output');
    const request = randomizeRequest(output.path);
    $('randomize-run').disabled = true;
    msg('randomize-result', 'Randomizando y exportando regalos…');
    const result = await post('/api/editors/gift/gen6/randomize', request);
    msg('randomize-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('randomize-result', error.message, 'error');
  } finally {
    updateRandomizerUi();
  }
}

$('browse').onclick = async () => {
  try {
    $('workspace').value = (await post('/api/workspace/pick')).path;
    await load();
  } catch (error) {
    msg('status', error.message, 'error');
  }
};
$('load').onclick = load;
$('open').onclick = open;
$('entry').onchange = () => {
  data = undefined;
  original = undefined;
  $('fields').replaceChildren();
  ui();
};
$('fields').oninput = event => {
  if (event.target.type === 'number')
    update(event.target);
};
$('fields').onchange = event => {
  if (event.target.type !== 'number')
    update(event.target);
};
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const entry = { ...data, IVs: data.ivs };
    delete entry.ivs;
    const result = await post('/api/editors/gift/gen6/export', {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      entryIndex: +$('entry').value,
      entry,
      language: 2,
    });
    original = structuredClone(data);
    msg('result', `Listo. ZIP: ${result.zipPath}`, 'success');
    ui();
  } catch (error) {
    msg('result', error.message, 'error');
  }
};
$('randomize-run').onclick = randomizeAll;
['rand-level-multiplier', 'rand-evolution-level', 'rand-seed'].forEach(id => {
  $(id).addEventListener('input', updateRandomizerUi);
});
ui();
