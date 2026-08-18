const $ = (id) => document.getElementById(id);
let game = null;
let catalog = null;
let current = null;
let original = null;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function setStatus(message, state = 'neutral') {
  $('status').textContent = message;
  $('status').className = `status ${state}`;
}

function fillSelect(select, entries, value) {
  select.replaceChildren();
  entries.forEach((entry) => {
    const option = document.createElement('option');
    option.value = entry.id;
    option.textContent = `${entry.name} · ${String(entry.id).padStart(3, '0')}`;
    select.append(option);
  });
  select.value = value;
}

function field(label, control) {
  const wrapper = document.createElement('label');
  wrapper.className = 'field';
  wrapper.append(document.createTextNode(label), control);
  return wrapper;
}

function numberInput(id, value, label) {
  const input = document.createElement('input');
  input.id = id;
  input.type = 'number';
  input.value = value;
  input.min = '0';
  return field(label, input);
}

function selectInput(id, value, entries, label) {
  const select = document.createElement('select');
  select.id = id;
  fillSelect(select, entries, value);
  return field(label, select);
}

function updateState() {
  const changed = current && JSON.stringify(readCurrent()) !== JSON.stringify(original);
  $('summary').textContent = changed ? 'Registro modificado' : 'Sin cambios para exportar';
  $('export').disabled = !game?.titleId || !changed;
}

function readCurrent() {
  if (!current) return null;
  if (current.kind === 'trainer') {
    return {
      trainerClass: Number($('maison-class').value),
      choices: $('maison-choices').value.split(',').map((value) => Number(value.trim())).filter((value) => Number.isInteger(value)),
    };
  }
  return {
    species: Number($('maison-species').value),
    form: Number($('maison-form').value),
    nature: Number($('maison-nature').value),
    item: Number($('maison-item').value),
    moves: [1, 2, 3, 4].map((index) => Number($(`maison-move-${index}`).value)),
    evs: [...document.querySelectorAll('[data-maison-ev]')].map((input) => input.checked),
  };
}

function renderTrainer(entry) {
  const details = $('details');
  details.replaceChildren();
  const grid = document.createElement('div');
  grid.className = 'maison-fields';
  grid.append(selectInput('maison-class', entry.trainerClass, catalog.classes, 'Clase'));
  const choices = document.createElement('input');
  choices.id = 'maison-choices';
  choices.value = entry.choices.join(', ');
  choices.placeholder = '0, 1, 2…';
  grid.append(field('Índices de Pokémon (separados por coma)', choices));
  const note = document.createElement('p');
  note.className = 'muted maison-note';
  note.textContent = 'Los índices apuntan a la lista de Pokémon del mismo modo de batalla. Se ordenan automáticamente al exportar.';
  details.append(grid, note);
}

function renderPokemon(entry) {
  const details = $('details');
  details.replaceChildren();
  const grid = document.createElement('div');
  grid.className = 'maison-fields';
  grid.append(selectInput('maison-species', entry.species, catalog.species, 'Especie'));
  grid.append(numberInput('maison-form', entry.form, 'Forma'));
  grid.append(selectInput('maison-nature', entry.nature, catalog.natures, 'Naturaleza'));
  grid.append(selectInput('maison-item', entry.item, catalog.items, 'Objeto'));
  for (let index = 0; index < 4; index++)
    grid.append(selectInput(`maison-move-${index + 1}`, entry.moves[index], catalog.moves, `Movimiento ${index + 1}`));

  const evBox = document.createElement('div');
  evBox.className = 'maison-evs';
  evBox.append(document.createElement('b'));
  evBox.firstChild.textContent = 'EVs entrenados';
  ['PS', 'Ataque', 'Defensa', 'Velocidad', 'Ataque especial', 'Defensa especial'].forEach((name, index) => {
    const label = document.createElement('label');
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.dataset.maisonEv = 'true';
    input.checked = entry.evs[index];
    label.append(input, document.createTextNode(name));
    evBox.append(label);
  });
  details.append(grid, evBox);
}

function populateCatalog() {
  fillSelect($('trainer'), catalog.trainers, catalog.trainers[0]?.id);
  fillSelect($('pokemon'), catalog.pokemon, catalog.pokemon[0]?.id);
  $('trainer').disabled = false;
  $('pokemon').disabled = false;
  $('open').disabled = false;
}

async function loadCatalog() {
  try {
    current = null;
    original = null;
    $('details').replaceChildren();
    updateState();
    game = await post('/api/workspace/inspect', { workspacePath: $('workspace').value });
    if (!['XY', 'ORAS', 'SM', 'USUM'].includes(game.gameVersion))
      throw new Error('Maison está disponible solo para X/Y, OR/AS y juegos de Gen. VII completos.');
    catalog = await post('/api/editors/maison/catalog', { workspacePath: $('workspace').value, variant: $('variant').value, language: 2 });
    populateCatalog();
    $('editor').classList.remove('is-disabled');
    setStatus(`${game.gameVersion} cargado: variante ${catalog.variant}.`, 'success');
  } catch (error) {
    game = null; catalog = null; current = null; original = null;
    $('editor').classList.add('is-disabled');
    $('details').replaceChildren();
    setStatus(error.message, 'error');
    updateState();
  }
}

async function openRecord() {
  if (!catalog) return;
  try {
    const kind = $('kind').value;
    const index = Number($(kind === 'trainer' ? 'trainer' : 'pokemon').value);
    const endpoint = kind === 'trainer' ? '/api/editors/maison/trainer' : '/api/editors/maison/pokemon';
    const data = await post(endpoint, { workspacePath: $('workspace').value, variant: $('variant').value, [`${kind}Index`]: index, language: 2 });
    current = { kind, index, entry: structuredClone(data.entry) };
    original = structuredClone(data.entry);
    kind === 'trainer' ? renderTrainer(current.entry) : renderPokemon(current.entry);
    setStatus(`${kind === 'trainer' ? 'Entrenador' : 'Pokémon'} ${index} abierto.`, 'success');
    updateState();
  } catch (error) {
    $('result').textContent = error.message;
    $('result').className = 'status error';
  }
}

$('browse').onclick = async () => {
  try { $('workspace').value = (await post('/api/workspace/pick')).path; await loadCatalog(); }
  catch (error) { setStatus(error.message, 'error'); }
};
$('load').onclick = loadCatalog;
$('variant').onchange = loadCatalog;
$('kind').onchange = () => {
  const trainer = $('kind').value === 'trainer';
  $('trainer-picker').classList.toggle('is-hidden', !trainer);
  $('pokemon-picker').classList.toggle('is-hidden', trainer);
};
$('open').onclick = openRecord;
$('details').oninput = updateState;
$('details').onchange = updateState;
$('export').onclick = async () => {
  try {
    const output = await post('/api/workspace/pick-output');
    const payload = {
      workspacePath: $('workspace').value,
      outputDirectory: output.path,
      titleId: game.titleId,
      variant: $('variant').value,
      [`${current.kind}Index`]: current.index,
      entry: readCurrent(),
      language: 2,
    };
    const endpoint = current.kind === 'trainer' ? '/api/editors/maison/trainer/export' : '/api/editors/maison/pokemon/export';
    const result = await post(endpoint, payload);
    original = structuredClone(readCurrent());
    $('result').textContent = `Listo. ZIP: ${result.zipPath}`;
    $('result').className = 'status success';
    updateState();
  } catch (error) {
    $('result').textContent = error.message;
    $('result').className = 'status error';
    updateState();
  }
};
$('kind').onchange();
updateState();
