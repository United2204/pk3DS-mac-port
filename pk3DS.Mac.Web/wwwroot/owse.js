const $ = id => document.getElementById(id);
let groups = [];
let catalog;
let zone;
let currentScript;
let currentMap;
let currentGen7Zone;
let currentGen7Entities;
let gen7EntitiesLoading = false;
let gen7EntitiesRequest = 0;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function msg(id, text, state = 'neutral') {
  $(id).textContent = text;
  $(id).className = `status ${state}`;
}

function selectedGroup() {
  const index = +$('group').value;
  return Number.isInteger(index) && index >= 0 ? groups[index] : undefined;
}

function clearEntry() {
  const empty = document.createElement('span');
  empty.className = 'muted';
  empty.textContent = 'Todavía no hay un script abierto.';
  $('metadata').replaceChildren(empty);
  const zoneEmpty = document.createElement('span');
  zoneEmpty.className = 'muted';
  zoneEmpty.textContent = 'Todavía no hay una zona abierta.';
  $('zone-metadata').replaceChildren(zoneEmpty);
  $('instructions').textContent = '—';
  $('parsed').textContent = '—';
  $('raw').textContent = '—';
  currentScript = undefined;
  $('script-fields').classList.add('is-hidden');
  $('script-instructions').value = '';
  $('export-script').disabled = true;
  msg('script-result', '');
  currentGen7Zone = undefined;
  $('gen7-zone-fields').classList.add('is-hidden');
  $('gen7-parent-map').disabled = true;
  $('export-zone7').disabled = true;
  msg('gen7-zone-result', '');
  currentGen7Entities = undefined;
  gen7EntitiesRequest += 1;
  gen7EntitiesLoading = false;
  $('gen7-entity-fields').classList.add('is-hidden');
  $('load-gen7-entities').disabled = true;
  $('load-gen7-entities').textContent = 'Leer posiciones';
  $('export-gen7-entities').disabled = true;
  $('gen7-entity-summary').replaceChildren();
  $('gen7-ep-table').replaceChildren();
  $('gen7-em-table').replaceChildren();
  $('gen7-eb-table').replaceChildren();
  $('gen7-es-table').replaceChildren();
  $('gen7-ea-table').replaceChildren();
  $('gen7-et-table').replaceChildren();
  msg('gen7-entity-result', '');
  currentMap = undefined;
  $('map-fields').classList.add('is-hidden');
  $('load-map').disabled = true;
  $('export-map').disabled = true;
  $('apply-map-cell').disabled = true;
  $('map-x').disabled = true;
  $('map-y').disabled = true;
  $('map-value').disabled = true;
  $('map-summary').replaceChildren();
  $('map-matrix').textContent = '—';
  msg('map-result', '');
  msg('result', '');
}

function syncScriptLimit() {
  const group = selectedGroup();
  const input = $('script-index');
  input.max = Math.max(0, (group?.scriptCount ?? 1) - 1);
  if (+input.value > +input.max) input.value = input.max;
}

function fillGroups() {
  const select = $('group');
  select.replaceChildren();
  groups.forEach((group, index) => select.add(new Option(`${group.name} · ${group.scriptCount} script(s)`, index)));
  select.disabled = !groups.length;
  $('script-index').disabled = !groups.length;
  $('open').disabled = !groups.length;
  $('load-zone').disabled = !groups.length;
  syncScriptLimit();
}

function renderRows(box, rows) {
  box.replaceChildren();
  for (const [label, value] of rows) {
    const row = document.createElement('div');
    row.className = 'owse-meta-row';
    const name = document.createElement('b');
    name.textContent = label;
    const content = document.createElement('span');
    content.textContent = String(value);
    row.append(name, content);
    box.append(row);
  }
}

function metadata(data) {
  renderRows($('metadata'), [
    ['Ubicación', data.locationName], ['Mundo', data.worldIndex], ['Script', data.scriptIndex],
    ['Bytes crudos', data.rawBytes], ['Magic', `0x${data.magic.toString(16).padStart(8, '0').toUpperCase()}`],
    ['Debug', data.debug ? 'sí' : 'no'],
    ['Inicio de instrucciones', `0x${data.scriptInstructionStart.toString(16).toUpperCase()}`],
    ['Inicio de movimiento', `0x${data.scriptMovementStart.toString(16).toUpperCase()}`],
    ['Offset final', `0x${data.finalOffset.toString(16).toUpperCase()}`],
    ['Memoria reservada', `0x${data.allocatedMemory.toString(16).toUpperCase()}`],
    ['Bytes comprimidos', data.compressedBytes], ['Bytes descomprimidos', data.decompressedBytes],
  ]);
}

function zoneMetadata(summary) {
  const box = $('zone-metadata');
  if (!summary) {
    const empty = document.createElement('span');
    empty.className = 'muted';
    empty.textContent = 'No se pudo describir la zona.';
    box.replaceChildren(empty);
    return;
  }
  const rows = [
    ['Índice de zona', summary.zoneIndex], ['Bytes de zonedata', summary.zoneDataBytes],
    ['Archivos de zona', summary.zoneFileCount], ['Mapa padre', summary.parentMap],
    ['Área de mapa', summary.mapArea], ['Matriz de mapa', summary.mapMatrix],
    ['Archivo de texto', summary.textFile], ['Archivo de script', summary.scriptFile],
    ['Clima', summary.weather], ['Muebles', summary.furnitureCount], ['NPC', summary.npcCount],
    ['Warp', summary.warpCount], ['Triggers', summary.triggerCount],
    ['Triggers desconocidos', summary.unknownEntityCount],
  ].filter(([, value]) => value !== null && value !== undefined);
  renderRows(box, rows);
  if (summary.diagnostics) {
    const row = document.createElement('div');
    row.className = 'owse-meta-row';
    const name = document.createElement('b');
    name.textContent = 'Diagnóstico';
    const content = document.createElement('span');
    content.textContent = summary.diagnostics;
    row.append(name, content);
    box.append(row);
  }
  if (summary.entityBlocks?.length) {
    const row = document.createElement('div');
    row.className = 'owse-meta-row';
    const name = document.createElement('b');
    name.textContent = 'Entidades Gen. VII';
    const content = document.createElement('span');
    content.textContent = summary.entityBlocks.map(formatGen7EntityBlock).join(' · ');
    row.append(name, content);
    box.append(row);
  }
}

function formatGen7EntityBlock(block) {
  const entries = (block.entries ?? []).map(entry => {
    const header = [];
    if (entry.recordCount !== null && entry.recordCount !== undefined)
      header.push(`cabecera=${entry.recordCount}`);
    if (entry.recordKind !== null && entry.recordKind !== undefined)
      header.push(`tipo=${entry.recordKind}`);
    return `#${entry.entryIndex}: ${entry.bytes} bytes${header.length ? ` (${header.join(', ')})` : ''}`;
  }).join(', ');
  const suffix = entries ? ` · ${entries}` : '';
  return `${block.identifier}: ${block.entryCount} entrada(s), ${block.bytes} bytes${block.isMiniArchive ? suffix : ' (opaco)'}`;
}

function render(data) {
  currentScript = data;
  metadata(data);
  zoneMetadata(data.zone);
  $('instructions').textContent = data.instructions.length
    ? data.instructions.map((value, index) => `${String(index).padStart(3, '0')}  0x${value.toString(16).padStart(8, '0').toUpperCase()}  ${value}`).join('\n')
    : '(sin instrucciones descomprimidas)';
  $('parsed').textContent = data.parsedLines.length ? data.parsedLines.join('\n') : '(sin líneas interpretadas)';
  $('raw').textContent = data.rawHex.length ? data.rawHex.join('\n') : '(sin bytes)';
  renderScriptEditor(data);
  renderGen7ZoneEditor(data);
  msg('result', data.parseError ? `Lectura completada con diagnóstico: ${data.parseError}` : `Script ${data.scriptIndex} leído correctamente.`, data.parseError ? 'error' : 'success');
}

function renderScriptEditor(data) {
  const editableGame = ['XY', 'ORAS', 'SM', 'USUM', 'US', 'UM', 'SN', 'MN'].includes(catalog?.gameVersion);
  const enabled = editableGame
    && data.instructions?.length > 0
    && !data.parseError;
  $('script-fields').classList.toggle('is-hidden', !enabled);
  $('export-script').disabled = !enabled;
  if (!enabled) {
    $('script-instructions').value = '';
    return;
  }
  $('script-instructions').value = data.instructions
    .map(value => `0x${value.toString(16).padStart(8, '0').toUpperCase()}`)
    .join('\n');
  msg('script-result', 'Podés editar los valores y exportar un parche LayeredFS.', 'neutral');
}

function renderGen7ZoneEditor(data) {
  const editableGame = ['SM', 'USUM', 'US', 'UM', 'SN', 'MN'].includes(catalog?.gameVersion);
  const enabled = editableGame && data.zone?.zoneIndex >= 0 && data.zone?.parentMap !== null && data.zone?.parentMap !== undefined;
  $('gen7-zone-fields').classList.toggle('is-hidden', !enabled);
  $('gen7-parent-map').disabled = !enabled;
  $('export-zone7').disabled = !enabled;
  $('load-gen7-entities').disabled = !enabled;
  if (!enabled) {
    $('gen7-entity-fields').classList.add('is-hidden');
    $('export-gen7-entities').disabled = true;
  } else {
    $('gen7-entity-fields').classList.remove('is-hidden');
    $('load-gen7-entities').onclick = loadGen7Entities;
    $('export-gen7-entities').onclick = exportGen7Entities;
    void loadGen7Entities();
  }
  if (!enabled) {
    currentGen7Zone = undefined;
    return;
  }
  currentGen7Zone = { zoneIndex: data.zone.zoneIndex, parentMap: data.zone.parentMap };
  $('gen7-parent-map').value = String(data.zone.parentMap);
  msg('gen7-zone-result', `Zona ${data.zone.zoneIndex} lista.`, 'neutral');
}

function renderGen7PositionTable(box, positions, label, includeBlock = false) {
  box.replaceChildren();
  if (!positions.length) {
    const empty = document.createElement('span');
    empty.className = 'muted';
    empty.textContent = `No se encontraron registros ${label} con formato confirmado en esta zona.`;
    box.append(empty);
    return;
  }
  const table = document.createElement('table');
  table.className = 'owse-entity-table';
  const header = document.createElement('tr');
  const columns = includeBlock
    ? ['#', 'Bloque ED', 'Contenedor', 'Registro', 'X', 'Y', 'Z']
    : ['#', 'Contenedor', 'Registro', 'X', 'Y', 'Z'];
  columns.forEach(text => {
    const cell = document.createElement('th');
    cell.textContent = text;
    header.append(cell);
  });
  const thead = document.createElement('thead');
  thead.append(header);
  table.append(thead);
  const body = document.createElement('tbody');
  positions.forEach((position, index) => {
    const row = document.createElement('tr');
    const indexCell = document.createElement('td');
    indexCell.textContent = String(index);
    row.append(indexCell);
    const indexKeys = includeBlock
      ? ['blockEntry', 'containerEntry', 'recordIndex']
      : ['containerEntry', 'recordIndex'];
    for (const key of indexKeys) {
      const cell = document.createElement('td');
      cell.textContent = String(position[key]);
      row.append(cell);
    }
    for (const axis of ['x', 'y', 'z']) {
      const cell = document.createElement('td');
      const input = document.createElement('input');
      input.type = 'number';
      input.step = 'any';
      input.value = String(position[axis]);
      input.dataset.position = String(index);
      input.dataset.axis = axis;
      input.title = `${axis.toUpperCase()} de la posición ${index}`;
      cell.append(input);
      row.append(cell);
    }
    body.append(row);
  });
  table.append(body);
  box.append(table);
}

function renderGen7Entities(data) {
  currentGen7Entities = data;
  const emPositions = data.emPositions ?? [];
  const ebPositions = data.ebPositions ?? [];
  const esPositions = data.esPositions ?? [];
  const eaPositions = data.eaPositions ?? [];
  const etPositions = data.etPositions ?? [];
  const summary = $('gen7-entity-summary');
  const blocks = (data.blocks ?? []).map(formatGen7EntityBlock);
  renderRows(summary, [
    ['Posiciones EP', data.positions.length],
    ['Posiciones EM principales', emPositions.length],
    ['Posiciones EB primarias', ebPositions.length],
    ['Posiciones ES primarias', esPositions.length],
    ['Posiciones EA tipo 5', eaPositions.length],
    ['Posiciones ET tipo 7', etPositions.length],
    ['Bloques ED', blocks.length ? blocks.join(' · ') : 'sin bloques interpretables'],
  ]);
  renderGen7PositionTable($('gen7-ep-table'), data.positions, 'EP');
  renderGen7PositionTable($('gen7-em-table'), emPositions, 'EM');
  renderGen7PositionTable($('gen7-eb-table'), ebPositions, 'EB', true);
  renderGen7PositionTable($('gen7-es-table'), esPositions, 'ES', true);
  renderGen7PositionTable($('gen7-ea-table'), eaPositions, 'EA', true);
  renderGen7PositionTable($('gen7-et-table'), etPositions, 'ET', true);
  $('load-gen7-entities').textContent = 'Volver a leer';
  $('gen7-entity-fields').classList.remove('is-hidden');
  $('export-gen7-entities').disabled = !(data.positions.length || emPositions.length || ebPositions.length || esPositions.length || eaPositions.length || etPositions.length);
  msg('gen7-entity-result', data.diagnostics || 'Posiciones EP, EM, EB, ES, EA y ET cargadas. Podés editar X/Y/Z y exportar un parche.', data.diagnostics ? 'neutral' : 'success');
}

async function loadGen7Entities() {
  if (gen7EntitiesLoading) return;
  const requestId = ++gen7EntitiesRequest;
  gen7EntitiesLoading = true;
  $('load-gen7-entities').disabled = true;
  try {
    if (!currentScript) throw Error('Primero abrí un script Gen. VII.');
    msg('gen7-entity-result', 'Leyendo posiciones EP, EM, EB, ES, EA y ET…');
    const data = await post('/api/editors/owse/gen7/entities', {
      workspacePath: $('workspace').value, worldIndex: currentScript.worldIndex, language: 2,
    });
    if (requestId !== gen7EntitiesRequest) return;
    renderGen7Entities(data);
  } catch (error) {
    if (requestId !== gen7EntitiesRequest) return;
    currentGen7Entities = undefined;
    $('gen7-entity-fields').classList.add('is-hidden');
    $('export-gen7-entities').disabled = true;
    $('load-gen7-entities').textContent = 'Reintentar lectura';
    msg('gen7-entity-result', error.message, 'error');
  } finally {
    if (requestId !== gen7EntitiesRequest) return;
    gen7EntitiesLoading = false;
    $('load-gen7-entities').disabled = !currentScript;
  }
}

function readGen7PositionInputs(positions, tableId, label) {
  return positions.map((position, index) => {
    const inputs = [...document.querySelectorAll(`#${tableId} input[data-position="${index}"]`)];
    const values = Object.fromEntries(inputs.map(input => [input.dataset.axis, Number(input.value)]));
    for (const axis of ['x', 'y', 'z']) {
      if (!Number.isFinite(values[axis])) throw Error(`La posición ${label} ${index} tiene un valor ${axis.toUpperCase()} inválido.`);
    }
    return { ...position, x: values.x, y: values.y, z: values.z };
  });
}

function readGen7EntityPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.positions, 'gen7-ep-table', 'EP');
}

function readGen7EmPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.emPositions ?? [], 'gen7-em-table', 'EM');
}

function readGen7EbPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.ebPositions ?? [], 'gen7-eb-table', 'EB');
}

function readGen7EsPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.esPositions ?? [], 'gen7-es-table', 'ES');
}

function readGen7EaPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.eaPositions ?? [], 'gen7-ea-table', 'EA');
}

function readGen7EtPositions() {
  if (!currentGen7Entities) throw Error('Primero leé las posiciones EP, EM, EB, ES, EA y ET.');
  return readGen7PositionInputs(currentGen7Entities.etPositions ?? [], 'gen7-et-table', 'ET');
}

async function exportGen7Entities() {
  try {
    if (!currentScript) throw Error('Primero abrí un script Gen. VII.');
    const positions = readGen7EntityPositions();
    const emPositions = readGen7EmPositions();
    const ebPositions = readGen7EbPositions();
    const esPositions = readGen7EsPositions();
    const eaPositions = readGen7EaPositions();
    const etPositions = readGen7EtPositions();
    const output = await post('/api/workspace/pick-output');
    msg('gen7-entity-result', 'Exportando posiciones EP, EM, EB, ES, EA y ET…');
    const result = await post('/api/editors/owse/gen7/entities/export', {
      workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
      worldIndex: currentScript.worldIndex, positions, emPositions, ebPositions, esPositions, eaPositions, etPositions, language: 2,
    });
    msg('gen7-entity-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('gen7-entity-result', error.message, 'error');
  }
}

function renderMap(data) {
  currentMap = data;
  $('map-fields').classList.remove('is-hidden');
  $('load-map').disabled = false;
  $('export-map').disabled = false;
  $('apply-map-cell').disabled = false;
  $('map-x').disabled = false;
  $('map-y').disabled = false;
  $('map-value').disabled = false;
  renderRows($('map-summary'), [
    ['Área GR', data.mapArea], ['Matriz MM', data.mapMatrix],
    ['Ancho', data.width], ['Alto', data.height], ['Celdas', data.properties.length],
    ['Ancho MM', data.matrixWidth], ['Alto MM', data.matrixHeight],
  ]);
  const matrixLines = data.matrixValues.length
    ? data.matrixValues.map((value, index) => `${String(index).padStart(3, '0')}  0x${value.toString(16).padStart(4, '0').toUpperCase()}`).join('\n')
    : '(sin celdas MM interpretadas)';
  $('map-matrix').textContent = matrixLines;
  $('map-x').max = Math.max(0, data.width - 1);
  $('map-y').max = Math.max(0, data.height - 1);
  const first = data.properties[0] ?? 0;
  $('map-value').value = `0x${first.toString(16).padStart(8, '0').toUpperCase()}`;
  msg('map-result', data.diagnostics || 'Mapa cargado. Seleccioná una celda para editarla.', data.diagnostics ? 'neutral' : 'success');
}

function renderEntityTable(title, collection, fields) {
  const section = document.createElement('section');
  section.className = 'owse-entity-group';
  const heading = document.createElement('h3');
  heading.textContent = `${title} · ${zone[collection].length}`;
  section.append(heading);
  const table = document.createElement('table');
  table.className = 'owse-entity-table';
  const header = document.createElement('tr');
  const indexHead = document.createElement('th');
  indexHead.textContent = '#';
  header.append(indexHead);
  fields.forEach(field => {
    const cell = document.createElement('th');
    cell.textContent = field.label;
    header.append(cell);
  });
  const thead = document.createElement('thead');
  thead.append(header);
  table.append(thead);
  const body = document.createElement('tbody');
  zone[collection].forEach((entry, index) => {
    const row = document.createElement('tr');
    const indexCell = document.createElement('td');
    indexCell.textContent = String(index);
    row.append(indexCell);
    fields.forEach(field => {
      const cell = document.createElement('td');
      const input = document.createElement('input');
      input.type = 'number';
      input.value = String(entry[field.key]);
      input.dataset.collection = collection;
      input.dataset.index = String(index);
      input.dataset.key = field.key;
      input.title = field.label;
      cell.append(input);
      row.append(cell);
    });
    body.append(row);
  });
  table.append(body);
  section.append(table);
  return section;
}

function renderZoneFields() {
  const box = $('zone-fields');
  box.replaceChildren();
  if (!zone?.metadata) {
    const empty = document.createElement('span');
    empty.className = 'muted';
    empty.textContent = 'Esta zona no expone los metadatos editables.';
    box.append(empty);
    return;
  }
  const fields = [
    ['mapArea', 'Área de mapa'], ['mapMatrix', 'Matriz de mapa'], ['textFile', 'Archivo de texto'],
    ['scriptFile', 'Archivo de script'], ['parentMap', 'Mapa padre'], ['weather', 'Clima'],
  ];
  fields.forEach(([key, label]) => {
    const field = document.createElement('label');
    field.textContent = label;
    const input = document.createElement('input');
    input.type = 'number';
    input.value = String(zone.metadata[key]);
    input.dataset.collection = 'metadata';
    input.dataset.key = key;
    field.append(input);
    box.append(field);
  });
}

function renderEntities() {
  const box = $('entity-fields');
  box.replaceChildren();
  renderZoneFields();
  if (!zone) {
    const empty = document.createElement('span');
    empty.className = 'muted';
    empty.textContent = 'Seleccioná una zona Gen. VI para editarla.';
    box.append(empty);
    $('export-zone').disabled = true;
    return;
  }
  $('map-fields').classList.remove('is-hidden');
  $('load-map').disabled = false;
  box.append(
    renderEntityTable('Muebles', 'furniture', [
      { key: 'script', label: 'Script' }, { key: 'x', label: 'X' }, { key: 'y', label: 'Y' },
      { key: 'width', label: 'Ancho' }, { key: 'height', label: 'Alto' },
    ]),
    renderEntityTable('NPC', 'npcs', [
      { key: 'id', label: 'ID' }, { key: 'model', label: 'Modelo' }, { key: 'spawnFlag', label: 'Spawn flag' },
      { key: 'script', label: 'Script' }, { key: 'faceDirection', label: 'Dirección' },
      { key: 'sightRange', label: 'Visión' }, { key: 'x', label: 'X' }, { key: 'y', label: 'Y' },
      { key: 'movePermissions', label: 'Movimiento' }, { key: 'movePermissions2', label: 'Movimiento 2' },
    ]),
    renderEntityTable('Warps', 'warps', [
      { key: 'destinationMap', label: 'Mapa destino' }, { key: 'destinationTileIndex', label: 'Tile destino' },
      { key: 'x', label: 'X' }, { key: 'y', label: 'Y' },
    ]),
    renderEntityTable('Triggers', 'triggers', [
      { key: 'script', label: 'Script' }, { key: 'constant', label: 'Constante' }, { key: 'type', label: 'Tipo' },
      { key: 'flags', label: 'Flags' }, { key: 'x', label: 'X' }, { key: 'y', label: 'Y' },
      { key: 'width', label: 'Ancho' }, { key: 'height', label: 'Alto' },
    ]),
    renderEntityTable('Triggers desconocidos', 'unknownTriggers', [
      { key: 'script', label: 'Script' }, { key: 'constant', label: 'Constante' }, { key: 'type', label: 'Tipo' },
      { key: 'flags', label: 'Flags' }, { key: 'x', label: 'X' }, { key: 'y', label: 'Y' },
      { key: 'width', label: 'Ancho' }, { key: 'height', label: 'Alto' },
    ]),
  );
  $('export-zone').disabled = false;
}

async function load() {
  try {
    msg('status', 'Leyendo grupos OWSE…');
    catalog = await post('/api/editors/owse/catalog', { workspacePath: $('workspace').value, language: 2 });
    groups = catalog.groups;
    fillGroups();
    clearEntry();
    const isGen6 = catalog.gameVersion === 'XY' || catalog.gameVersion === 'ORAS';
    $('gen6-editor').classList.toggle('is-hidden', !isGen6);
    zone = undefined;
    renderEntities();
    msg('status', `${catalog.gameVersion} cargado. ${groups.length} grupo(s) de scripts detectado(s).`, groups.length ? 'success' : 'neutral');
  } catch (error) {
    catalog = undefined;
    groups = [];
    zone = undefined;
    fillGroups();
    renderEntities();
    $('gen6-editor').classList.add('is-hidden');
    clearEntry();
    msg('status', error.message, 'error');
  }
}

async function open() {
  try {
    const group = selectedGroup();
    if (!group) throw Error('No hay un grupo de scripts disponible.');
    const scriptIndex = +$('script-index').value;
    if (scriptIndex < 0 || scriptIndex >= group.scriptCount) throw Error('El índice de script indicado no existe en este grupo.');
    msg('result', 'Leyendo script…');
    const data = await post('/api/editors/owse/entry', {
      workspacePath: $('workspace').value, group: group.id, worldIndex: group.worldIndex, scriptIndex, language: 2,
    });
    render(data);
  } catch (error) {
    msg('result', error.message, 'error');
  }
}

async function loadZone() {
  try {
    const group = selectedGroup();
    if (!group) throw Error('No hay una zona seleccionada.');
    msg('entity-result', 'Leyendo entidades de zona…');
    zone = await post('/api/editors/owse/gen6/zone', {
      workspacePath: $('workspace').value, zoneIndex: group.worldIndex, language: 2,
    });
    renderEntities();
    await loadMap();
    msg('entity-result', `Zona ${zone.zoneIndex} lista para editar.`, 'success');
  } catch (error) {
    zone = undefined;
    renderEntities();
    msg('entity-result', error.message, 'error');
  }
}

async function loadMap() {
  try {
    if (!zone) throw Error('Primero abrí una zona Gen. VI.');
    msg('map-result', 'Leyendo grilla de movimiento…');
    const data = await post('/api/editors/owse/gen6/map', {
      workspacePath: $('workspace').value, zoneIndex: zone.zoneIndex, language: 2,
    });
    renderMap(data);
  } catch (error) {
    currentMap = undefined;
    $('export-map').disabled = true;
    $('apply-map-cell').disabled = true;
    msg('map-result', error.message, 'error');
  }
}

async function exportZone() {
  try {
    if (!zone) throw Error('Primero abrí una zona Gen. VI.');
    const output = await post('/api/workspace/pick-output');
    msg('entity-result', 'Exportando parche OWSE…');
    const result = await post('/api/editors/owse/gen6/export', {
      workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
      zoneIndex: zone.zoneIndex, furniture: zone.furniture, npcs: zone.npcs, warps: zone.warps,
      triggers: zone.triggers, unknownTriggers: zone.unknownTriggers, metadata: zone.metadata, language: 2,
    });
    msg('entity-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('entity-result', error.message, 'error');
  }
}

function parseScriptInstructions() {
  if (!currentScript?.instructions?.length) throw Error('Primero abrí un script editable.');
  const lines = $('script-instructions').value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
  if (lines.length !== currentScript.instructions.length) {
    throw Error(`La cantidad de instrucciones debe ser ${currentScript.instructions.length}; se encontraron ${lines.length}.`);
  }
  return lines.map((line, index) => {
    const token = line.split(/\s+/)[0];
    const value = /^0x[0-9a-f]+$/i.test(token) ? Number.parseInt(token.slice(2), 16) : Number(token);
    if (!Number.isInteger(value) || value < 0 || value > 0xFFFFFFFF) {
      throw Error(`La instrucción ${index + 1} no es un uint32 válido: ${token}`);
    }
    return value >>> 0;
  });
}

async function exportScript() {
  try {
    if (!currentScript) throw Error('Primero abrí un script.');
    const instructions = parseScriptInstructions();
    const output = await post('/api/workspace/pick-output');
    msg('script-result', 'Exportando parche del script…');
    const isGen6 = currentScript.group?.startsWith('gen6-');
    const result = await post(
      isGen6 ? '/api/editors/owse/gen6/script' : '/api/editors/owse/script/export',
      isGen6
        ? {
            workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
            group: currentScript.group, zoneIndex: currentScript.worldIndex, instructions, language: 2,
          }
        : {
            workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
            group: currentScript.group, worldIndex: currentScript.worldIndex,
            scriptIndex: currentScript.scriptIndex, instructions, language: 2,
          });
    msg('script-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('script-result', error.message, 'error');
  }
}

async function exportGen7Zone() {
  try {
    if (!currentGen7Zone) throw Error('Primero abrí un script con una zona Gen. VII válida.');
    const parentMap = Number($('gen7-parent-map').value);
    if (!Number.isInteger(parentMap) || parentMap < 0) throw Error('El mapa padre debe ser un entero no negativo.');
    const output = await post('/api/workspace/pick-output');
    msg('gen7-zone-result', 'Exportando metadatos de zona…');
    const result = await post('/api/editors/owse/gen7/zone/export', {
      workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
      zoneIndex: currentGen7Zone.zoneIndex, parentMap, language: 2,
    });
    msg('gen7-zone-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('gen7-zone-result', error.message, 'error');
  }
}

function mapCellIndex() {
  if (!currentMap) throw Error('Primero leé el mapa de la zona.');
  const x = Number($('map-x').value);
  const y = Number($('map-y').value);
  if (!Number.isInteger(x) || !Number.isInteger(y) || x < 0 || y < 0 || x >= currentMap.width || y >= currentMap.height)
    throw Error(`La celda debe estar dentro de ${currentMap.width} × ${currentMap.height}.`);
  return (y * currentMap.width) + x;
}

function parseUint32Value(raw, label) {
  const token = raw.trim();
  const value = /^0x[0-9a-f]+$/i.test(token) ? Number.parseInt(token.slice(2), 16) : Number(token);
  if (!Number.isInteger(value) || value < 0 || value > 0xFFFFFFFF)
    throw Error(`${label} no es un uint32 válido: ${token}`);
  return value >>> 0;
}

function applyMapCell() {
  try {
    const index = mapCellIndex();
    currentMap.properties[index] = parseUint32Value($('map-value').value, 'La propiedad');
    $('map-value').value = `0x${currentMap.properties[index].toString(16).padStart(8, '0').toUpperCase()}`;
    msg('map-result', 'Hay cambios sin exportar.', 'neutral');
  } catch (error) {
    msg('map-result', error.message, 'error');
  }
}

async function exportMap() {
  try {
    if (!currentMap) throw Error('Primero leé el mapa de la zona.');
    const output = await post('/api/workspace/pick-output');
    msg('map-result', 'Exportando propiedades del mapa…');
    const result = await post('/api/editors/owse/gen6/map/export', {
      workspacePath: $('workspace').value, outputDirectory: output.path, titleId: null,
      zoneIndex: currentMap.zoneIndex, properties: currentMap.properties, language: 2,
    });
    msg('map-result', `Listo. ZIP: ${result.zipPath}`, 'success');
  } catch (error) {
    msg('map-result', error.message, 'error');
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
$('load-zone').onclick = loadZone;
$('load-map').onclick = loadMap;
$('export-zone').onclick = exportZone;
$('export-script').onclick = exportScript;
$('export-zone7').onclick = exportGen7Zone;
$('load-gen7-entities').onclick = loadGen7Entities;
$('export-gen7-entities').onclick = exportGen7Entities;
$('export-map').onclick = exportMap;
$('apply-map-cell').onclick = applyMapCell;
$('group').onchange = () => { syncScriptLimit(); zone = undefined; clearEntry(); renderEntities(); };
$('script-instructions').oninput = () => msg('script-result', 'Hay cambios sin exportar.', 'neutral');
$('gen7-parent-map').oninput = () => msg('gen7-zone-result', 'Hay cambios sin exportar.', 'neutral');
$('entity-fields').oninput = event => {
  const input = event.target;
  const collection = input.dataset.collection;
  if (!collection || !zone) return;
  if (collection === 'metadata') zone.metadata[input.dataset.key] = Number(input.value);
  else zone[collection][+input.dataset.index][input.dataset.key] = Number(input.value);
  msg('entity-result', 'Hay cambios sin exportar.', 'neutral');
};
