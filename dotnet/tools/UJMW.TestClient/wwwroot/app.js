(function () {
  'use strict';

  const KIND_INTERFACE = 'Interface';
  const KIND_STATIC = 'Static';

  // ======================================================================
  //  host bridge (requests: {id, cmd, args} / replies: {id, ok, result, error})
  // ======================================================================

  const pendingRequests = new Map();
  let nextRequestId = 1;

  function callHost(command, args) {
    return new Promise((resolve, reject) => {
      const id = nextRequestId++;
      pendingRequests.set(id, { resolve: resolve, reject: reject });
      let effectiveArgs = args;
      if (!effectiveArgs) {
        effectiveArgs = {};
      }
      window.chrome.webview.postMessage({ id: id, cmd: command, args: effectiveArgs });
    });
  }

  function callHostWithFiles(command, files) {
    return new Promise((resolve, reject) => {
      const id = nextRequestId++;
      pendingRequests.set(id, { resolve: resolve, reject: reject });
      window.chrome.webview.postMessageWithAdditionalObjects({ id: id, cmd: command, args: {} }, files);
    });
  }

  window.chrome.webview.addEventListener('message', (event) => {
    const reply = event.data;
    const pending = pendingRequests.get(reply.id);
    if (!pending) {
      return;
    }
    pendingRequests.delete(reply.id);
    if (reply.ok) {
      pending.resolve(reply.result);
    } else {
      pending.reject(new Error(reply.error));
    }
  });

  function showError(error) {
    if (error && error.message) {
      alert(error.message);
    } else {
      alert(String(error));
    }
  }

  // ======================================================================
  //  ui state
  // ======================================================================

  const ui = {
    state: null,
    selectedKey: null,
    collapsedKeys: new Set(),
    collapsedNamespaceKeys: new Set(),
    detail: null,
    infrastructure: null
  };

  let sideChannelSaveTimer = null;

  // ======================================================================
  //  generic helpers
  // ======================================================================

  function el(tag, className, text) {
    const element = document.createElement(tag);
    if (className) {
      element.className = className;
    }
    if (text !== undefined && text !== null) {
      element.textContent = text;
    }
    return element;
  }

  function button(text, onClick, className, title) {
    const element = el('button', className, text);
    element.type = 'button';
    if (title) {
      element.title = title;
    }
    element.addEventListener('click', onClick);
    return element;
  }

  function textInput(value, placeholder) {
    const input = el('input');
    input.type = 'text';
    input.spellcheck = false;
    if (value !== undefined && value !== null) {
      input.value = value;
    }
    if (placeholder) {
      input.placeholder = placeholder;
    }
    return input;
  }

  function fieldRow(labelText) {
    const row = el('div', 'field-row');
    if (labelText !== null) {
      row.appendChild(el('label', null, labelText));
    }
    return row;
  }

  function makeKey(parts) {
    return JSON.stringify(parts);
  }

  function compareText(a, b) {
    return a.localeCompare(b, undefined, { sensitivity: 'base' });
  }

  function normalizePresetName(name) {
    return name.split('/').map((s) => s.trim()).filter((s) => s.length > 0).join('/');
  }

  function shortTypeName(fullName) {
    return fullName.substring(fullName.lastIndexOf('.') + 1).replace(/\+/g, '.');
  }

  function kindClass(kind) {
    return 'kind-' + String(kind).toLowerCase();
  }

  function kindIcon(kind) {
    if (kind === KIND_INTERFACE) {
      return '\u{1F539}';
    }
    return '\u{1F538}';
  }

  function hostOf(url) {
    try {
      return new URL(url).hostname;
    } catch {
      return 'unknown-host';
    }
  }

  function clearDetailPanel() {
    const panel = document.getElementById('detailPanel');
    panel.innerHTML = '';
    return panel;
  }

  function showDetailPlaceholder() {
    const panel = clearDetailPanel();
    panel.appendChild(el('div', 'placeholder', 'Select a DLL or a type on the left.'));
  }

  function findDll(path) {
    return ui.state.dlls.find((d) => d.path === path);
  }

  function isTypeChecked(path, fullName) {
    const dll = findDll(path);
    if (!dll) {
      return false;
    }
    return dll.checkedTypes.some((t) => t.fullName === fullName);
  }

  function applyState(state) {
    ui.state = state;
    renderMainTree();
    refreshTemplateSelect();
  }

  // ======================================================================
  //  tree rendering
  // ======================================================================

  function setToggleState(toggle, childList, collapsed) {
    if (collapsed) {
      childList.classList.add('hidden');
      toggle.textContent = '\u25B8';
    } else {
      childList.classList.remove('hidden');
      toggle.textContent = '\u25BE';
    }
  }

  function createTreeNode(collapsedSet, options) {
    const li = el('li', 'tree-node');
    const row = el('div', 'tree-row');
    if (options.cssClass) {
      row.className = 'tree-row ' + options.cssClass;
    }
    row.dataset.key = options.key;
    if (options.title) {
      row.title = options.title;
    }
    if (ui.selectedKey === options.key) {
      row.classList.add('selected');
    }

    const toggle = el('span', 'tree-toggle');
    let childList = null;
    if (options.hasChildren) {
      childList = el('ul', 'tree-list');
      setToggleState(toggle, childList, collapsedSet.has(options.key));
      const toggleCollapsed = (event) => {
        event.stopPropagation();
        const collapse = !childList.classList.contains('hidden');
        if (collapse) {
          collapsedSet.add(options.key);
        } else {
          collapsedSet.delete(options.key);
        }
        setToggleState(toggle, childList, collapse);
      };
      toggle.addEventListener('click', toggleCollapsed);
      row.addEventListener('dblclick', toggleCollapsed);
    }

    const icon = el('span', 'tree-icon', options.icon);
    if (options.iconClass) {
      icon.classList.add(options.iconClass);
    }
    row.append(toggle, icon, el('span', 'tree-label', options.label));

    if (options.onSelect) {
      row.addEventListener('click', () => {
        options.onSelect(row);
      });
    }
    if (options.onContextMenu) {
      row.addEventListener('contextmenu', (event) => {
        event.preventDefault();
        event.stopPropagation();
        options.onContextMenu(event);
      });
    }

    li.appendChild(row);
    if (childList) {
      li.appendChild(childList);
    }
    return { li: li, row: row, icon: icon, childList: childList };
  }

  function markSelected(key) {
    ui.selectedKey = key;
    document.querySelectorAll('#mainTree .tree-row').forEach((row) => {
      if (row.dataset.key === key) {
        row.classList.add('selected');
      } else {
        row.classList.remove('selected');
      }
    });
  }

  function renderMainTree() {
    const container = document.getElementById('mainTree');
    const scrollTop = container.scrollTop;
    container.innerHTML = '';
    const rootList = el('ul', 'tree-list');

    ui.state.dlls.forEach((dll) => {
      let dllClass = 'dll';
      if (!dll.exists) {
        dllClass = 'dll missing';
      }
      let dllTitle = dll.path;
      if (!dll.exists) {
        dllTitle = dll.path + ' (file not found)';
      }
      const dllNode = createTreeNode(ui.collapsedKeys, {
        key: makeKey(['dll', dll.path]),
        label: dll.displayName,
        icon: '\u{1F4E6}',
        cssClass: dllClass,
        title: dllTitle,
        hasChildren: dll.checkedTypes.length > 0,
        onSelect: () => {
          selectDll(dll);
        }
      });

      dll.checkedTypes.forEach((checkedType) => {
        const presets = ui.state.presets.filter((p) => p.typeFullName === checkedType.fullName);
        const typeNode = createTreeNode(ui.collapsedKeys, {
          key: makeKey(['type', dll.path, checkedType.fullName]),
          label: shortTypeName(checkedType.fullName),
          icon: kindIcon(checkedType.kind),
          cssClass: kindClass(checkedType.kind),
          title: checkedType.fullName,
          hasChildren: presets.length > 0,
          onSelect: () => {
            selectType(dll, checkedType);
          }
        });
        if (presets.length > 0) {
          appendPresetLevel(typeNode.childList, buildPresetHierarchy(presets), dll, checkedType);
        }
        dllNode.childList.appendChild(typeNode.li);
      });

      rootList.appendChild(dllNode.li);
    });

    container.appendChild(rootList);
    container.scrollTop = scrollTop;
  }

  function buildPresetHierarchy(presets) {
    const root = { name: '', path: '', folders: new Map(), presets: [] };
    presets.forEach((preset) => {
      const segments = preset.name.split('/');
      let current = root;
      for (let i = 0; i < segments.length - 1; i++) {
        const segment = segments[i];
        let folderPath = segment;
        if (current.path) {
          folderPath = current.path + '/' + segment;
        }
        if (!current.folders.has(segment)) {
          current.folders.set(segment, { name: segment, path: folderPath, folders: new Map(), presets: [] });
        }
        current = current.folders.get(segment);
      }
      current.presets.push({ leafName: segments[segments.length - 1], preset: preset });
    });
    return root;
  }

  function countPresets(folder) {
    let count = folder.presets.length;
    folder.folders.forEach((child) => {
      count += countPresets(child);
    });
    return count;
  }

  function appendPresetLevel(list, level, dll, checkedType) {
    Array.from(level.folders.keys()).sort(compareText).forEach((folderName) => {
      const folder = level.folders.get(folderName);
      const folderNode = createTreeNode(ui.collapsedKeys, {
        key: makeKey(['folder', dll.path, checkedType.fullName, folder.path]),
        label: folder.name,
        icon: '\u{1F4C1}',
        cssClass: 'folder',
        title: folder.path,
        hasChildren: true,
        onContextMenu: (event) => {
          showFolderContextMenu(event, checkedType.fullName, folder);
        }
      });
      appendPresetLevel(folderNode.childList, folder, dll, checkedType);
      list.appendChild(folderNode.li);
    });

    level.presets.slice().sort((a, b) => compareText(a.leafName, b.leafName)).forEach((entry) => {
      const presetNode = createTreeNode(ui.collapsedKeys, {
        key: makeKey(['preset', dll.path, checkedType.fullName, entry.preset.name]),
        label: entry.leafName,
        icon: '\u2B50',
        cssClass: 'preset',
        title: entry.preset.name,
        hasChildren: false,
        onSelect: () => {
          selectPreset(dll, checkedType, entry.preset);
        },
        onContextMenu: (event) => {
          showPresetContextMenu(event, checkedType.fullName, entry.preset);
        }
      });
      list.appendChild(presetNode.li);
    });
  }

  // ======================================================================
  //  selection
  // ======================================================================

  function selectDll(dll) {
    markSelected(makeKey(['dll', dll.path]));
    ui.detail = null;
    renderDllDetail(dll);
  }

  function selectType(dll, checkedType) {
    markSelected(makeKey(['type', dll.path, checkedType.fullName]));
    renderTypeDetail(dll, checkedType, null);
  }

  function selectPreset(dll, checkedType, preset) {
    markSelected(makeKey(['preset', dll.path, checkedType.fullName, preset.name]));
    const detail = ui.detail;
    if (detail && detail.loaded && detail.dllPath === dll.path && detail.typeFullName === checkedType.fullName) {
      applyPreset(preset);
    } else {
      renderTypeDetail(dll, checkedType, preset);
    }
  }

  // ======================================================================
  //  detail: DLL (type selection)
  // ======================================================================

  function renderDllDetail(dll) {
    const panel = clearDetailPanel();
    const header = el('div', 'detail-header');
    header.append(el('h2', null, dll.displayName), el('span', 'sub', dll.path));
    panel.appendChild(header);

    if (!dll.exists) {
      panel.appendChild(el('div', 'warning', 'The file does not exist (anymore). Select it and press Del to remove it from the list.'));
      return;
    }

    panel.appendChild(el('div', 'info', 'Click a class or interface to check / uncheck it. Checked types are shown below the DLL on the left.'));
    const treeContainer = el('div', 'tree type-tree');
    treeContainer.appendChild(el('div', 'info', 'Loading types\u2026'));
    panel.appendChild(treeContainer);

    const expectedKey = ui.selectedKey;
    callHost('getDllTypes', { path: dll.path }).then((namespaces) => {
      if (ui.selectedKey !== expectedKey) {
        return;
      }
      treeContainer.innerHTML = '';
      if (namespaces.length === 0) {
        treeContainer.appendChild(el('div', 'info', 'No public classes or interfaces found.'));
        return;
      }
      treeContainer.appendChild(buildNamespaceTree(dll, namespaces));
    }).catch((error) => {
      treeContainer.innerHTML = '';
      treeContainer.appendChild(el('div', 'warning', error.message));
    });
  }

  function buildNamespaceTree(dll, namespaces) {
    const root = { name: '', path: '', children: new Map(), types: [] };
    namespaces.forEach((ns) => {
      let current = root;
      if (ns.name !== '') {
        ns.name.split('.').forEach((segment) => {
          if (!current.children.has(segment)) {
            let childPath = segment;
            if (current.path) {
              childPath = current.path + '.' + segment;
            }
            current.children.set(segment, { name: segment, path: childPath, children: new Map(), types: [] });
          }
          current = current.children.get(segment);
        });
      }
      current.types = current.types.concat(ns.types);
    });
    const list = el('ul', 'tree-list');
    appendNamespaceLevel(list, root, dll);
    return list;
  }

  function appendNamespaceLevel(list, level, dll) {
    Array.from(level.children.keys()).sort(compareText).forEach((childName) => {
      const child = level.children.get(childName);
      const node = createTreeNode(ui.collapsedNamespaceKeys, {
        key: makeKey(['ns', dll.path, child.path]),
        label: child.name,
        icon: '{ }',
        cssClass: 'namespace',
        title: child.path,
        hasChildren: true
      });
      appendNamespaceLevel(node.childList, child, dll);
      list.appendChild(node.li);
    });

    level.types.forEach((type) => {
      const isStatic = type.kind === KIND_STATIC;
      let title = type.fullName + ' (' + type.kind + ')';
      if (isStatic) {
        title = type.fullName + ' (static class - not selectable)';
      }
      const options = {
        key: makeKey(['dt', dll.path, type.fullName]),
        label: type.displayName,
        icon: checkIcon(isTypeChecked(dll.path, type.fullName), isStatic),
        iconClass: 'check',
        cssClass: kindClass(type.kind),
        title: title,
        hasChildren: false
      };
      if (!isStatic) {
        options.onSelect = (row) => {
          toggleTypeSelection(dll, type, row);
        };
      }
      list.appendChild(createTreeNode(new Set(), options).li);
    });
  }

  function checkIcon(checked, isStatic) {
    if (isStatic) {
      return '\u2012';
    }
    if (checked) {
      return '\u2611';
    }
    return '\u2610';
  }

  function toggleTypeSelection(dll, type, row) {
    callHost('toggleType', { path: dll.path, fullName: type.fullName, kind: type.kind }).then((state) => {
      applyState(state);
      row.querySelector('.tree-icon').textContent = checkIcon(isTypeChecked(dll.path, type.fullName), false);
    }).catch(showError);
  }

  // ======================================================================
  //  detail: type (URL, auth, sidechannel, test client)
  // ======================================================================

  function createHistoryCombo(placeholder) {
    const wrapper = el('div', 'combo');
    const input = textInput('', placeholder);
    const list = el('div', 'combo-list hidden');
    let items = [];

    const render = () => {
      list.innerHTML = '';
      if (items.length === 0) {
        list.appendChild(el('div', 'combo-empty', '(no history)'));
      }
      items.forEach((item) => {
        const entry = el('div', 'combo-item', item);
        entry.addEventListener('mousedown', (event) => {
          event.preventDefault();
          input.value = item;
          list.classList.add('hidden');
          input.dispatchEvent(new Event('input'));
        });
        list.appendChild(entry);
      });
    };

    const toggleList = () => {
      if (list.classList.contains('hidden')) {
        render();
        list.classList.remove('hidden');
      } else {
        list.classList.add('hidden');
      }
    };

    input.addEventListener('keydown', (event) => {
      if (event.key === 'ArrowDown' && event.altKey) {
        event.preventDefault();
        toggleList();
      } else if (event.key === 'Escape') {
        list.classList.add('hidden');
      }
    });

    wrapper.append(input, button('\u25BE', toggleList, 'combo-toggle', 'Show history (Alt+Down)'), list);
    return {
      wrapper: wrapper,
      input: input,
      setItems: (newItems) => {
        items = newItems;
        render();
      }
    };
  }

  function renderTypeDetail(dll, checkedType, presetToLoad) {
    const panel = clearDetailPanel();
    const detail = {
      dllPath: dll.path,
      typeFullName: checkedType.fullName,
      kind: checkedType.kind,
      methods: [],
      loaded: false,
      loadedPresetName: null,
      elements: {}
    };
    ui.detail = detail;
    const e = detail.elements;

    // header
    const header = el('div', 'detail-header');
    const title = el('h2', kindClass(checkedType.kind), shortTypeName(checkedType.fullName));
    if (checkedType.kind === KIND_INTERFACE) {
      title.style.color = '#0050c8';
    }
    header.append(
      title,
      el('span', 'sub', checkedType.fullName + '   \u00B7   ' + dll.displayName),
      el('span', 'spacer'),
      button('\u2605', saveCurrentAsPreset, 'btn-star', 'Save URL, authorization, sidechannel and request as preset')
    );
    panel.appendChild(header);

    e.warningBox = el('div', 'warning hidden');
    panel.appendChild(e.warningBox);

    e.typeSummary = el('div', 'doc-summary hidden');
    panel.appendChild(e.typeSummary);

    // zone
    const zoneRow = fieldRow('Zone');
    e.zoneInput = textInput(ui.state.currentZone, 'Infrastructure zone (select or type), replaces {zone} in the SideChannel');
    e.zoneInput.setAttribute('list', 'zoneOptions');
    e.zoneInput.addEventListener('change', () => {
      ui.state.currentZone = e.zoneInput.value.trim();
      callHost('setCurrentZone', { zone: ui.state.currentZone }).catch(showError);
    });
    e.resolveButton = button('Resolve\u2026', openResolveDialog, null, 'Resolve the URL via the managed infrastructure');
    e.resolveButton.disabled = !infrastructureCanResolve();
    zoneRow.append(e.zoneInput, e.resolveButton);
    panel.appendChild(zoneRow);

    // wellknown
    const wellknownRow = fieldRow('Wellknown');
    e.wellknownSelect = el('select');
    e.wellknownSelect.addEventListener('change', applySelectedWellknown);
    e.wellknownAddButton = button('New\u2026', openWellknownDialog, null, 'Add a wellknown endpoint for this contract');
    e.wellknownDeleteButton = button('Delete', deleteSelectedWellknown, null, 'Delete the selected wellknown endpoint');
    wellknownRow.append(e.wellknownSelect, e.wellknownAddButton, e.wellknownDeleteButton);
    panel.appendChild(wellknownRow);

    // URL
    const urlRow = fieldRow('URL');
    e.urlCombo = createHistoryCombo('Service base URL, e.g. https://host/api/MyService');
    e.urlInput = e.urlCombo.input;
    e.urlInput.addEventListener('input', updateAfterUrlVisibility);
    urlRow.append(
      e.urlCombo.wrapper,
      button('\u{1F310}', openUrlInBrowser, 'icon', 'Open URL in browser')
    );
    panel.appendChild(urlRow);

    e.afterUrl = el('div', 'after-url');
    panel.appendChild(e.afterUrl);

    // authorization
    const authRow = fieldRow('Authorization');
    e.authInput = textInput('', 'Authorization header value, e.g. Bearer eyJ\u2026');
    authRow.append(e.authInput, button('Issue\u2026', openIssueTokenDialog, null, 'Generate a JWT or request a token via OAuth code flow'));
    e.afterUrl.appendChild(authRow);

    // sidechannel
    const sideChannelSection = el('details', 'section');
    sideChannelSection.appendChild(el('summary', null, 'SideChannel'));
    const sideChannelBody = el('div', 'section-body');
    const templateRow = fieldRow('Template');
    e.templateSelect = el('select');
    templateRow.append(
      e.templateSelect,
      button('Load', loadSelectedTemplate),
      button('Save as template\u2026', saveSideChannelTemplate),
      button('Delete', deleteSelectedTemplate)
    );
    e.sideChannelInput = el('textarea', 'code');
    e.sideChannelInput.spellcheck = false;
    e.sideChannelInput.value = ui.state.sideChannelText;
    e.sideChannelInput.addEventListener('input', () => {
      scheduleSideChannelSave(e.sideChannelInput.value);
    });
    sideChannelBody.append(
      templateRow,
      e.sideChannelInput,
      el('div', 'info', 'This JSON object replaces the {AUTO} token of the "_" property immediately before the request is sent. The placeholder {zone} is replaced by the selected zone.')
    );
    sideChannelSection.appendChild(sideChannelBody);
    e.afterUrl.appendChild(sideChannelSection);

    // test client
    const testClient = el('section', 'test-client');
    testClient.appendChild(el('h3', null, 'Test client'));
    const methodRow = fieldRow('Method');
    e.methodSelect = el('select');
    e.methodSelect.addEventListener('change', applyMethodTemplate);
    methodRow.appendChild(e.methodSelect);
    e.signature = el('div', 'signature code');
    e.methodSummary = el('div', 'doc-summary hidden');
    e.requestInput = el('textarea', 'code editor');
    e.requestInput.id = 'requestInput';
    e.requestInput.spellcheck = false;
    e.requestInput.wrap = 'off';
    const executeRow = el('div', 'execute-row');
    e.executeButton = button('EXECUTE', executeRequest, 'primary');
    e.executeStatus = el('span', 'execute-status');
    executeRow.append(e.executeButton, e.executeStatus);
    e.resultOutput = el('textarea', 'code editor');
    e.resultOutput.id = 'resultOutput';
    e.resultOutput.readOnly = true;
    e.resultOutput.spellcheck = false;
    e.resultOutput.wrap = 'off';

    const testClientColumns = el('div', 'test-client-columns');
    const editorColumn = el('div', 'editor-column');
    editorColumn.append(e.requestInput, executeRow, e.resultOutput);
    e.docColumn = el('div', 'doc-column');
    testClientColumns.append(editorColumn, e.docColumn);
    testClient.append(methodRow, e.signature, e.methodSummary, testClientColumns);
    e.afterUrl.appendChild(testClient);

    refreshTemplateSelect();
    loadWellknownEndpoints();

    callHost('getTypeDetail', { path: dll.path, typeFullName: checkedType.fullName }).then((result) => {
      if (ui.detail !== detail) {
        return;
      }
      detail.methods = result.methods;
      detail.loaded = true;
      e.methodSelect.innerHTML = '';
      result.methods.forEach((method, index) => {
        const option = el('option', null, method.name);
        option.value = String(index);
        option.title = method.signature;
        e.methodSelect.appendChild(option);
      });
      if (result.warning) {
        e.warningBox.textContent = result.warning;
        e.warningBox.classList.remove('hidden');
      }
      setDocText(e.typeSummary, result.summary);
      e.urlCombo.setItems(result.urlHistory);
      if (presetToLoad) {
        applyPreset(presetToLoad);
      } else {
        if (result.urlHistory.length > 0) {
          e.urlInput.value = result.urlHistory[0];
        }
        applyMethodTemplate();
      }
      updateAfterUrlVisibility();
    }).catch((error) => {
      e.warningBox.textContent = error.message;
      e.warningBox.classList.remove('hidden');
    });
  }

  function updateAfterUrlVisibility() {
    const detail = ui.detail;
    if (!detail) {
      return;
    }
    const hasUrl = detail.elements.urlInput.value.trim().length > 0;
    const afterUrl = detail.elements.afterUrl;
    if (hasUrl) {
      afterUrl.classList.remove('disabled');
      afterUrl.title = '';
    } else {
      afterUrl.classList.add('disabled');
      afterUrl.title = 'Enter a URL first.';
    }
    afterUrl.querySelectorAll('input, select, textarea, button').forEach((control) => {
      control.disabled = !hasUrl;
    });
  }

  function currentMethod() {
    const detail = ui.detail;
    const index = parseInt(detail.elements.methodSelect.value, 10);
    if (isNaN(index)) {
      return null;
    }
    return detail.methods[index];
  }

  function applyMethodTemplate() {
    const e = ui.detail.elements;
    const method = currentMethod();
    if (method) {
      e.requestInput.value = method.requestTemplate;
    } else {
      e.requestInput.value = '';
    }
    renderMethodDocumentation();
  }

  function setDocText(element, text) {
    element.textContent = text || '';
    if (text) {
      element.classList.remove('hidden');
    } else {
      element.classList.add('hidden');
    }
  }

  function renderMethodDocumentation() {
    const e = ui.detail.elements;
    const method = currentMethod();
    e.docColumn.innerHTML = '';
    if (!method) {
      e.signature.textContent = '';
      setDocText(e.methodSummary, '');
      return;
    }
    e.signature.textContent = method.signature;
    setDocText(e.methodSummary, method.summary);

    const parameters = method.parameters || [];
    const inputParameters = parameters.filter((p) => p.direction !== 'out');
    const outputParameters = parameters.filter((p) => p.direction !== 'in');

    const appendEntry = (container, name, typeName, description, suffix) => {
      const entry = el('div', 'doc-entry');
      const head = el('div', 'doc-entry-head');
      head.append(el('span', 'doc-name', name), el('span', 'doc-type', typeName + (suffix || '')));
      entry.appendChild(head);
      if (description) {
        entry.appendChild(el('div', 'doc-text', description));
      } else {
        entry.appendChild(el('div', 'doc-text doc-missing', '(no description)'));
      }
      container.appendChild(entry);
    };

    const requestDocs = el('div', 'doc-block');
    requestDocs.appendChild(el('div', 'doc-title', 'Request parameters'));
    if (inputParameters.length === 0) {
      requestDocs.appendChild(el('div', 'doc-text doc-missing', '(none)'));
    }
    inputParameters.forEach((p) => {
      let suffix = '';
      if (p.direction === 'ref') {
        suffix = ' (ref)';
      } else if (p.isOptional) {
        suffix = ' (optional)';
      }
      appendEntry(requestDocs, p.name, p.typeName, p.description, suffix);
    });

    const responseDocs = el('div', 'doc-block');
    responseDocs.appendChild(el('div', 'doc-title', 'Response'));
    if (method.returnTypeName && method.returnTypeName !== 'void') {
      appendEntry(responseDocs, 'return', method.returnTypeName, method.returnsDescription);
    }
    outputParameters.forEach((p) => {
      appendEntry(responseDocs, p.name, p.typeName, p.description, ' (' + p.direction + ')');
    });
    if (responseDocs.childNodes.length === 1) {
      responseDocs.appendChild(el('div', 'doc-text doc-missing', '(void)'));
    }
    e.docColumn.append(requestDocs, responseDocs);
  }

  function applyPreset(preset) {
    const detail = ui.detail;
    const e = detail.elements;
    detail.loadedPresetName = preset.name;
    e.urlInput.value = preset.url || '';
    e.authInput.value = preset.authHeader || '';
    e.sideChannelInput.value = preset.sideChannel || '';

    const methodIndex = detail.methods.findIndex((m) => m.name === preset.methodName);
    if (methodIndex >= 0) {
      e.methodSelect.value = String(methodIndex);
    }
    renderMethodDocumentation();
    if (preset.requestJson) {
      e.requestInput.value = preset.requestJson;
    } else {
      applyMethodTemplate();
    }
    e.resultOutput.value = '';
    e.resultOutput.className = 'code';
    updateAfterUrlVisibility();
  }

  function setUrlHistory(history) {
    if (ui.detail) {
      ui.detail.elements.urlCombo.setItems(history);
    }
  }

  function touchCurrentUrl() {
    const detail = ui.detail;
    const url = detail.elements.urlInput.value.trim();
    return callHost('touchUrl', { typeFullName: detail.typeFullName, url: url }).then((history) => {
      setUrlHistory(history);
      return url;
    });
  }

  function openUrlInBrowser() {
    const url = ui.detail.elements.urlInput.value.trim();
    if (!url) {
      return;
    }
    touchCurrentUrl().then(() => callHost('openBrowser', { url: url })).catch(showError);
  }

  function executeRequest() {
    const detail = ui.detail;
    const e = detail.elements;
    const method = currentMethod();
    const url = e.urlInput.value.trim();
    if (!method) {
      alert('Please select a method.');
      return;
    }
    if (!url) {
      alert('Please enter a URL.');
      return;
    }

    e.executeButton.disabled = true;
    e.executeStatus.textContent = 'Executing ' + method.name + '\u2026';
    e.resultOutput.value = '';
    e.resultOutput.className = 'code';

    touchCurrentUrl().then(() => callHost('execute', {
      typeFullName: detail.typeFullName,
      url: url,
      methodName: method.name,
      authHeader: e.authInput.value,
      sideChannel: e.sideChannelInput.value,
      zone: e.zoneInput.value.trim(),
      requestText: e.requestInput.value
    })).then((result) => {
      ui.state.sideChannelText = e.sideChannelInput.value;
      e.resultOutput.value = result.text;
      e.resultOutput.className = 'code result-' + String(result.kind).toLowerCase();
    }).catch((error) => {
      e.resultOutput.value = error.message;
      e.resultOutput.className = 'code result-error';
    }).finally(() => {
      e.executeButton.disabled = false;
      e.executeStatus.textContent = '';
    });
  }

  function scheduleSideChannelSave(text) {
    if (sideChannelSaveTimer) {
      clearTimeout(sideChannelSaveTimer);
    }
    sideChannelSaveTimer = setTimeout(() => {
      sideChannelSaveTimer = null;
      ui.state.sideChannelText = text;
      callHost('setSideChannelText', { text: text }).catch(showError);
    }, 700);
  }

  // ======================================================================
  //  presets
  // ======================================================================

  function saveCurrentAsPreset() {
    const detail = ui.detail;
    if (!detail || !detail.loaded) {
      return;
    }
    const e = detail.elements;
    const method = currentMethod();
    let methodName = '';
    if (method) {
      methodName = method.name;
    }

    let suggestion = detail.loadedPresetName;
    if (!suggestion) {
      suggestion = hostOf(e.urlInput.value.trim()) + '/' + methodName;
    }
    const enteredName = prompt('Preset name ("/" creates folders):', suggestion);
    if (enteredName === null) {
      return;
    }
    const name = normalizePresetName(enteredName);
    if (!name) {
      alert('The preset name must not be empty.');
      return;
    }
    const exists = ui.state.presets.some((p) => p.typeFullName === detail.typeFullName && p.name === name);
    if (exists && !confirm('The preset "' + name + '" already exists.\n\nOverwrite it?')) {
      return;
    }

    const preset = {
      name: name,
      typeFullName: detail.typeFullName,
      url: e.urlInput.value.trim(),
      authHeader: e.authInput.value,
      sideChannel: e.sideChannelInput.value,
      methodName: methodName,
      requestJson: e.requestInput.value
    };
    callHost('savePreset', { preset: preset }).then((state) => {
      detail.loadedPresetName = name;
      ui.selectedKey = makeKey(['preset', detail.dllPath, detail.typeFullName, name]);
      applyState(state);
    }).catch(showError);
  }

  function adjustLoadedPresetName(typeFullName, oldName, newName, isFolder) {
    const detail = ui.detail;
    if (!detail || detail.typeFullName !== typeFullName || !detail.loadedPresetName) {
      return;
    }
    if (isFolder) {
      if (detail.loadedPresetName.startsWith(oldName + '/')) {
        const relativeName = detail.loadedPresetName.substring(oldName.length + 1);
        if (newName) {
          detail.loadedPresetName = newName + '/' + relativeName;
        } else {
          detail.loadedPresetName = relativeName;
        }
      }
    } else if (detail.loadedPresetName === oldName) {
      detail.loadedPresetName = newName;
    }
  }

  function showFolderContextMenu(event, typeFullName, folder) {
    showContextMenu(event, [
      {
        label: 'Rename\u2026',
        action: () => {
          const entered = prompt('Rename folder (full path, "/" separates folders):', folder.path);
          if (entered === null) {
            return;
          }
          const newPath = normalizePresetName(entered);
          if (newPath === folder.path) {
            return;
          }
          callHost('renameFolder', { typeFullName: typeFullName, oldPath: folder.path, newPath: newPath }).then((state) => {
            adjustLoadedPresetName(typeFullName, folder.path, newPath, true);
            applyState(state);
          }).catch(showError);
        }
      },
      {
        label: 'Delete',
        action: () => {
          const count = countPresets(folder);
          if (!confirm('Delete the folder "' + folder.path + '" including ' + count + ' preset(s)?')) {
            return;
          }
          callHost('deleteFolder', { typeFullName: typeFullName, path: folder.path }).then((state) => {
            const detail = ui.detail;
            if (detail && detail.typeFullName === typeFullName && detail.loadedPresetName && detail.loadedPresetName.startsWith(folder.path + '/')) {
              detail.loadedPresetName = null;
            }
            applyState(state);
          }).catch(showError);
        }
      }
    ]);
  }

  function showPresetContextMenu(event, typeFullName, preset) {
    showContextMenu(event, [
      {
        label: 'Rename / move\u2026',
        action: () => {
          const entered = prompt('Rename / move preset (full path, "/" separates folders):', preset.name);
          if (entered === null) {
            return;
          }
          const newName = normalizePresetName(entered);
          if (!newName || newName === preset.name) {
            return;
          }
          callHost('renamePreset', { typeFullName: typeFullName, oldName: preset.name, newName: newName }).then((state) => {
            adjustLoadedPresetName(typeFullName, preset.name, newName, false);
            applyState(state);
          }).catch(showError);
        }
      },
      {
        label: 'Delete',
        action: () => {
          if (!confirm('Delete the preset "' + preset.name + '"?')) {
            return;
          }
          callHost('deletePreset', { typeFullName: typeFullName, name: preset.name }).then((state) => {
            const detail = ui.detail;
            if (detail && detail.typeFullName === typeFullName && detail.loadedPresetName === preset.name) {
              detail.loadedPresetName = null;
            }
            applyState(state);
          }).catch(showError);
        }
      }
    ]);
  }

  // ======================================================================
  //  sidechannel templates
  // ======================================================================

  function refreshTemplateSelect() {
    const detail = ui.detail;
    if (!detail || !detail.elements.templateSelect) {
      return;
    }
    const select = detail.elements.templateSelect;
    const previousValue = select.value;
    select.innerHTML = '';
    const emptyOption = el('option', null, '(select template)');
    emptyOption.value = '';
    select.appendChild(emptyOption);
    ui.state.sideChannelTemplates.forEach((template) => {
      const option = el('option', null, template.name);
      option.value = template.name;
      select.appendChild(option);
    });
    if (ui.state.sideChannelTemplates.some((t) => t.name === previousValue)) {
      select.value = previousValue;
    }
  }

  function loadSelectedTemplate() {
    const e = ui.detail.elements;
    const template = ui.state.sideChannelTemplates.find((t) => t.name === e.templateSelect.value);
    if (!template) {
      return;
    }
    e.sideChannelInput.value = template.content;
    scheduleSideChannelSave(template.content);
  }

  function saveSideChannelTemplate() {
    const e = ui.detail.elements;
    const enteredName = prompt('Template name:', e.templateSelect.value);
    if (enteredName === null) {
      return;
    }
    const name = enteredName.trim();
    if (!name) {
      return;
    }
    const exists = ui.state.sideChannelTemplates.some((t) => t.name === name);
    if (exists && !confirm('The template "' + name + '" already exists.\n\nOverwrite it?')) {
      return;
    }
    callHost('saveSideChannelTemplate', { name: name, content: e.sideChannelInput.value }).then((state) => {
      applyState(state);
      e.templateSelect.value = name;
    }).catch(showError);
  }

  function deleteSelectedTemplate() {
    const e = ui.detail.elements;
    const name = e.templateSelect.value;
    if (!name) {
      return;
    }
    if (!confirm('Delete the template "' + name + '"?')) {
      return;
    }
    callHost('deleteSideChannelTemplate', { name: name }).then(applyState).catch(showError);
  }

  // ======================================================================
  //  context menu & modal
  // ======================================================================

  function showContextMenu(event, items) {
    const menu = document.getElementById('contextMenu');
    menu.innerHTML = '';
    items.forEach((item) => {
      const entry = el('div', 'item', item.label);
      entry.addEventListener('click', () => {
        hideContextMenu();
        item.action();
      });
      menu.appendChild(entry);
    });
    menu.classList.remove('hidden');
    let left = event.clientX;
    let top = event.clientY;
    if (left + menu.offsetWidth > window.innerWidth) {
      left = window.innerWidth - menu.offsetWidth - 2;
    }
    if (top + menu.offsetHeight > window.innerHeight) {
      top = window.innerHeight - menu.offsetHeight - 2;
    }
    menu.style.left = left + 'px';
    menu.style.top = top + 'px';
  }

  function hideContextMenu() {
    document.getElementById('contextMenu').classList.add('hidden');
  }

  function openModal(title, body, buttons) {
    const layer = document.getElementById('modalLayer');
    layer.innerHTML = '';
    const modal = el('div', 'modal');
    const bodyContainer = el('div', 'modal-body');
    bodyContainer.appendChild(body);
    const buttonBar = el('div', 'modal-buttons');
    const close = () => {
      layer.classList.add('hidden');
      layer.innerHTML = '';
    };
    buttons.forEach((definition) => {
      buttonBar.appendChild(button(definition.label, () => {
        definition.action(close);
      }, definition.className));
    });
    modal.append(el('div', 'modal-title', title), bodyContainer, buttonBar);
    layer.appendChild(modal);
    layer.classList.remove('hidden');
    return close;
  }

  function isModalOpen() {
    return !document.getElementById('modalLayer').classList.contains('hidden');
  }

  function deepCopy(value) {
    return JSON.parse(JSON.stringify(value));
  }

  function createTabs(definitions) {
    const container = el('div');
    const tabBar = el('div', 'tabs');
    const panes = [];
    const tabs = [];
    definitions.forEach((definition, index) => {
      const tab = el('div', 'tab', definition.label);
      const pane = definition.pane;
      if (index === 0) {
        tab.classList.add('active');
      } else {
        pane.classList.add('hidden');
      }
      tab.addEventListener('click', () => {
        tabs.forEach((t) => t.classList.remove('active'));
        panes.forEach((p) => p.classList.add('hidden'));
        tab.classList.add('active');
        pane.classList.remove('hidden');
      });
      tabs.push(tab);
      panes.push(pane);
      tabBar.appendChild(tab);
    });
    container.appendChild(tabBar);
    panes.forEach((pane) => container.appendChild(pane));
    return container;
  }

  // ======================================================================
  //  managed infrastructure (zones, hosts, oauth, wellknown, resolve)
  // ======================================================================

  function emptyInfrastructure() {
    return {
      capabilities: { isReadOnly: true, canResolve: false },
      zones: [],
      hosts: [],
      oauthConfigurations: [],
      error: null
    };
  }

  function infrastructureCanResolve() {
    return !!(ui.infrastructure && ui.infrastructure.capabilities && ui.infrastructure.capabilities.canResolve);
  }

  function infrastructureIsReadOnly() {
    if (!ui.infrastructure || !ui.infrastructure.capabilities) {
      return true;
    }
    return !!ui.infrastructure.capabilities.isReadOnly;
  }

  function applyInfrastructure(infrastructure) {
    ui.infrastructure = infrastructure;
    if (!ui.infrastructure.error) {
      ui.infrastructure.error = null;
    }
    refreshZoneOptions();
    refreshInfrastructureDependentControls();
  }

  function loadInfrastructure() {
    return callHost('getInfrastructure').then(applyInfrastructure).catch((error) => {
      const infrastructure = emptyInfrastructure();
      infrastructure.error = error.message;
      applyInfrastructure(infrastructure);
    });
  }

  function flattenZones(zones, prefix, target) {
    (zones || []).forEach((zone) => {
      let path = zone.name;
      if (prefix) {
        path = prefix + ' / ' + zone.name;
      }
      target.push({ name: zone.name, path: path, description: zone.description });
      flattenZones(zone.childZones, path, target);
    });
    return target;
  }

  function refreshZoneOptions() {
    const dataList = document.getElementById('zoneOptions');
    dataList.innerHTML = '';
    flattenZones(ui.infrastructure.zones, null, []).forEach((zone) => {
      const option = el('option');
      option.value = zone.name;
      option.label = zone.path;
      dataList.appendChild(option);
    });
  }

  function refreshInfrastructureDependentControls() {
    const detail = ui.detail;
    if (!detail) {
      return;
    }
    const e = detail.elements;
    e.resolveButton.disabled = !infrastructureCanResolve();
    if (infrastructureCanResolve()) {
      e.resolveButton.title = 'Resolve the URL via the managed infrastructure';
    } else {
      e.resolveButton.title = 'The current infrastructure store does not support url resolution';
    }
    e.wellknownAddButton.disabled = infrastructureIsReadOnly();
    e.wellknownDeleteButton.disabled = infrastructureIsReadOnly() || !e.wellknownSelect.value;
  }

  function loadWellknownEndpoints() {
    const detail = ui.detail;
    if (!detail) {
      return Promise.resolve();
    }
    return callHost('getWellknownEndpoints', { contractName: detail.typeFullName }).then((endpoints) => {
      if (ui.detail === detail) {
        renderWellknownEndpoints(endpoints);
      }
    }).catch((error) => {
      if (ui.detail === detail) {
        renderWellknownEndpoints([]);
        detail.elements.wellknownSelect.title = error.message;
      }
    });
  }

  function renderWellknownEndpoints(endpoints) {
    const detail = ui.detail;
    const e = detail.elements;
    detail.wellknownEndpoints = endpoints;
    e.wellknownSelect.innerHTML = '';
    let placeholderText = '(select a wellknown endpoint)';
    if (endpoints.length === 0) {
      placeholderText = '(no wellknown endpoints for this contract)';
    }
    const placeholder = el('option', null, placeholderText);
    placeholder.value = '';
    e.wellknownSelect.appendChild(placeholder);
    endpoints.forEach((endpoint, index) => {
      let label = endpoint.url;
      if (endpoint.webApplicationName) {
        label = endpoint.webApplicationName + '  \u2013  ' + endpoint.url;
      }
      const option = el('option', null, label);
      option.value = String(index);
      e.wellknownSelect.appendChild(option);
    });
    refreshInfrastructureDependentControls();
  }

  function selectedWellknownEndpoint() {
    const detail = ui.detail;
    const index = parseInt(detail.elements.wellknownSelect.value, 10);
    if (isNaN(index)) {
      return null;
    }
    return detail.wellknownEndpoints[index];
  }

  function applySelectedWellknown() {
    const endpoint = selectedWellknownEndpoint();
    refreshInfrastructureDependentControls();
    if (!endpoint) {
      return;
    }
    applyResolvedUrl(endpoint.url);
  }

  function applyResolvedUrl(url) {
    ui.detail.elements.urlInput.value = url;
    updateAfterUrlVisibility();
    touchCurrentUrl().catch(showError);
  }

  function deleteSelectedWellknown() {
    const endpoint = selectedWellknownEndpoint();
    if (!endpoint) {
      return;
    }
    if (!confirm('Delete the wellknown endpoint "' + endpoint.url + '"?')) {
      return;
    }
    callHost('deleteWellknownEndpoint', {
      contractName: endpoint.contractName,
      hostUrl: endpoint.hostUrl,
      webApplicationName: endpoint.webApplicationName
    }).then(renderWellknownEndpoints).catch(showError);
  }

  function openWellknownDialog() {
    const detail = ui.detail;
    const endpoint = {
      contractName: detail.typeFullName,
      hostUrl: '',
      webApplicationName: '',
      endpointUrl: ''
    };
    const hosts = (ui.infrastructure && ui.infrastructure.hosts) || [];
    const body = el('div');
    body.appendChild(el('div', 'info', 'Maps the contract "' + detail.typeFullName + '" to a known host / web application. The resulting URL is <host>/<applicationBaseUrl>/<endpointUrl>.'));

    const hostRow = fieldRow('Host');
    const hostSelect = el('select');
    hosts.forEach((host) => {
      const option = el('option', null, host.hostUrl);
      option.value = host.hostUrl;
      hostSelect.appendChild(option);
    });
    hostRow.appendChild(hostSelect);

    const applicationRow = fieldRow('Web application');
    const applicationSelect = el('select');
    applicationRow.appendChild(applicationSelect);

    const endpointRow = fieldRow('Endpoint URL');
    const endpointInput = textInput('', 'relative endpoint url of the contract, e.g. MyService');
    endpointRow.appendChild(endpointInput);

    const preview = el('div', 'info code');

    const findHost = () => hosts.find((h) => h.hostUrl === hostSelect.value);
    const findApplication = () => {
      const host = findHost();
      if (!host) {
        return null;
      }
      return (host.webApplications || []).find((a) => a.name === applicationSelect.value);
    };
    const updatePreview = () => {
      const parts = [];
      if (hostSelect.value) {
        parts.push(hostSelect.value.trim().replace(/\/+$/, ''));
      }
      const application = findApplication();
      if (application && application.applicationBaseUrl) {
        parts.push(application.applicationBaseUrl.trim().replace(/^\/+|\/+$/g, ''));
      }
      if (endpointInput.value.trim()) {
        parts.push(endpointInput.value.trim().replace(/^\/+|\/+$/g, ''));
      }
      preview.textContent = parts.filter((p) => p.length > 0).join('/');
    };
    const renderApplications = () => {
      applicationSelect.innerHTML = '';
      const none = el('option', null, '(none)');
      none.value = '';
      applicationSelect.appendChild(none);
      const host = findHost();
      if (host) {
        (host.webApplications || []).forEach((application) => {
          const option = el('option', null, application.name + '  (' + application.applicationBaseUrl + ')');
          option.value = application.name;
          applicationSelect.appendChild(option);
        });
      }
      updatePreview();
    };
    hostSelect.addEventListener('change', renderApplications);
    applicationSelect.addEventListener('change', updatePreview);
    endpointInput.addEventListener('input', updatePreview);
    renderApplications();

    if (hosts.length === 0) {
      body.appendChild(el('div', 'warning', 'No hosts are defined yet. Use "\u2699 Settings" \u2192 "Hosts" to define them.'));
    }
    body.append(hostRow, applicationRow, endpointRow, el('div', 'info', 'Resulting URL'), preview);

    openModal('New wellknown endpoint', body, [
      {
        label: 'Save',
        className: 'primary',
        action: (close) => {
          endpoint.hostUrl = hostSelect.value;
          endpoint.webApplicationName = applicationSelect.value;
          endpoint.endpointUrl = endpointInput.value.trim();
          if (!endpoint.hostUrl) {
            alert('Please select a host.');
            return;
          }
          callHost('saveWellknownEndpoint', { endpoint: endpoint }).then((endpoints) => {
            close();
            if (ui.detail === detail) {
              renderWellknownEndpoints(endpoints);
            }
          }).catch(showError);
        }
      },
      { label: 'Cancel', action: (close) => close() }
    ]);
  }

  function openResolveDialog() {
    const detail = ui.detail;
    const zone = detail.elements.zoneInput.value.trim();
    if (!zone) {
      alert('Please select or enter a zone first.');
      return;
    }
    detail.elements.resolveButton.disabled = true;
    callHost('resolveUrls', { contractName: detail.typeFullName, zone: zone }).then((urls) => {
      if (ui.detail !== detail) {
        return;
      }
      if (urls.length === 0) {
        alert('No URL was resolved for "' + detail.typeFullName + '" within zone "' + zone + '".');
        return;
      }
      if (urls.length === 1) {
        applyResolvedUrl(urls[0]);
        return;
      }
      const body = el('div');
      body.appendChild(el('div', 'info', 'Multiple URLs were resolved - please select one:'));
      const urlList = el('div', 'url-list');
      let closeDialog = null;
      urls.forEach((url) => {
        const item = el('div', 'url-item', url);
        item.addEventListener('click', () => {
          applyResolvedUrl(url);
          closeDialog();
        });
        urlList.appendChild(item);
      });
      body.appendChild(urlList);
      closeDialog = openModal('Resolve URL \u2013 ' + shortTypeName(detail.typeFullName) + ' @ ' + zone, body, [
        { label: 'Cancel', action: (close) => close() }
      ]);
    }).catch(showError).finally(() => {
      refreshInfrastructureDependentControls();
    });
  }

  // ======================================================================
  //  dialog: settings (client, infrastructure source, zones, hosts, oauth)
  // ======================================================================

  function setReadOnly(container, readOnly) {
    container.querySelectorAll('input, select, textarea, button').forEach((control) => {
      if (!control.classList.contains('always-enabled')) {
        control.disabled = readOnly;
      }
    });
  }

  function saveButton(label, onClick) {
    const element = button(label, onClick, 'primary');
    return element;
  }

  function buildClientPane() {
    const pane = el('div');
    const timeoutRow = fieldRow('Timeout (s)');
    const timeoutInput = el('input');
    timeoutInput.type = 'number';
    timeoutInput.min = '1';
    timeoutInput.value = String(ui.state.requestTimeoutSeconds);
    timeoutRow.appendChild(timeoutInput);
    pane.append(
      timeoutRow,
      el('div', 'info', 'Maximum duration of a single request which is executed by the test client (default: 5 seconds).'),
      saveButton('Save', () => {
        const seconds = parseInt(timeoutInput.value, 10);
        if (isNaN(seconds) || seconds < 1) {
          alert('The timeout must be at least 1 second.');
          return;
        }
        callHost('setClientSettings', { requestTimeoutSeconds: seconds }).then(() => {
          ui.state.requestTimeoutSeconds = seconds;
        }).catch(showError);
      })
    );
    return pane;
  }

  function buildSourcePane(onSourceChanged) {
    const settings = deepCopy(ui.state.infrastructureSettings);
    const pane = el('div');
    pane.appendChild(el('div', 'info', 'Zones, hosts, OAuth configurations and wellknown endpoints are provided by an infrastructure store (IUjmwManagedInfrastructure). User specific data (presets, templates, JWT inputs) always stays local.'));

    const modeRow = fieldRow('Source');
    const modeSelect = el('select');
    [
      ['Local', 'Local (stored on this machine)'],
      ['External', 'Externally managed (UJMW service)'],
      ['Reflection', 'Implementation from a DLL next to the client']
    ].forEach((entry) => {
      const option = el('option', null, entry[1]);
      option.value = entry[0];
      modeSelect.appendChild(option);
    });
    modeSelect.value = settings.mode || 'Local';
    modeRow.appendChild(modeSelect);

    const externalPane = el('div');
    const urlRow = fieldRow('Service URL');
    urlRow.appendChild(bindText(textInput('', 'https://host/api/UjmwManagedInfrastructure'), settings, 'externalUrl'));
    const authRow = fieldRow('Authorization');
    authRow.appendChild(bindText(textInput('', 'Bearer token or plain token'), settings, 'externalAuthHeader'));
    externalPane.append(urlRow, authRow);

    const reflectionPane = el('div');
    const typeRow = fieldRow('Implementation');
    const typeSelect = el('select');
    typeRow.append(typeSelect, button('Search', () => searchImplementations(), null, 'Search DLLs next to the client for implementations'));
    reflectionPane.appendChild(typeRow);
    const searchImplementations = () => {
      typeSelect.innerHTML = '';
      callHost('findInfrastructureImplementations').then((candidates) => {
        if (candidates.length === 0) {
          const option = el('option', null, '(no implementations found)');
          option.value = '';
          typeSelect.appendChild(option);
        }
        candidates.forEach((candidate) => {
          const option = el('option', null, candidate.split(',')[0]);
          option.value = candidate;
          option.title = candidate;
          typeSelect.appendChild(option);
        });
        if (settings.reflectionTypeName && candidates.indexOf(settings.reflectionTypeName) >= 0) {
          typeSelect.value = settings.reflectionTypeName;
        }
      }).catch(showError);
    };
    if (settings.reflectionTypeName) {
      const option = el('option', null, settings.reflectionTypeName.split(',')[0]);
      option.value = settings.reflectionTypeName;
      typeSelect.appendChild(option);
    }

    const updateModeVisibility = () => {
      externalPane.classList.toggle('hidden', modeSelect.value !== 'External');
      reflectionPane.classList.toggle('hidden', modeSelect.value !== 'Reflection');
    };
    modeSelect.addEventListener('change', () => {
      updateModeVisibility();
      if (modeSelect.value === 'Reflection' && typeSelect.options.length === 0) {
        searchImplementations();
      }
    });
    updateModeVisibility();

    const status = el('div', 'info');
    const capabilities = ui.infrastructure.capabilities;
    if (ui.infrastructure.error) {
      status.textContent = 'Current store is not available: ' + ui.infrastructure.error;
      status.className = 'warning';
    } else {
      let text = 'Current store: ';
      if (capabilities.isReadOnly) {
        text = text + 'read-only';
      } else {
        text = text + 'editable';
      }
      if (capabilities.canResolve) {
        text = text + ', supports url resolution';
      } else {
        text = text + ', no url resolution';
      }
      status.textContent = text;
    }

    pane.append(modeRow, externalPane, reflectionPane, saveButton('Apply', () => {
      settings.mode = modeSelect.value;
      settings.reflectionTypeName = typeSelect.value;
      callHost('saveInfrastructureSettings', { infrastructure: settings }).then((state) => {
        applyState(state);
        return loadInfrastructure();
      }).then(() => {
        loadWellknownEndpoints();
        onSourceChanged();
      }).catch(showError);
    }), status);
    return pane;
  }

  function buildZonesPane() {
    const zones = deepCopy(ui.infrastructure.zones || []);
    const pane = el('div');
    pane.appendChild(el('div', 'info', 'Zones are maintained as area tree. The selected zone is used for resolving and replaces {zone} in the SideChannel.'));
    const treeContainer = el('div', 'zone-tree');

    const renderLevel = (list, siblings, depth) => {
      siblings.forEach((zone, index) => {
        if (!zone.childZones) {
          zone.childZones = [];
        }
        const row = el('div', 'zone-row');
        row.style.paddingLeft = (depth * 20) + 'px';
        row.append(
          bindText(textInput('', 'Name'), zone, 'name'),
          bindText(textInput('', 'Description'), zone, 'description'),
          button('+ child', () => {
            zone.childZones.push({ name: '', description: '', childZones: [] });
            render();
          }, null, 'Add a child zone'),
          button('\u2715', () => {
            siblings.splice(index, 1);
            render();
          }, 'icon', 'Remove zone (including child zones)')
        );
        list.appendChild(row);
        renderLevel(list, zone.childZones, depth + 1);
      });
    };
    const render = () => {
      treeContainer.innerHTML = '';
      renderLevel(treeContainer, zones, 0);
      setReadOnly(treeContainer, infrastructureIsReadOnly());
    };
    render();

    const addButton = button('+ Add zone', () => {
      zones.push({ name: '', description: '', childZones: [] });
      render();
    });
    const save = saveButton('Save zones', () => {
      callHost('saveInfrastructureZones', { zones: zones }).then(applyInfrastructure).catch(showError);
    });
    addButton.disabled = infrastructureIsReadOnly();
    save.disabled = infrastructureIsReadOnly();
    pane.append(treeContainer, addButton, el('div', 'button-bar'), save);
    return pane;
  }

  function buildHostsPane() {
    const hosts = deepCopy(ui.infrastructure.hosts || []);
    const pane = el('div');
    pane.appendChild(el('div', 'info', 'Hosts are plain server URLs. Each host can contain web applications (name + application base URL).'));
    const container = el('div');

    const render = () => {
      container.innerHTML = '';
      hosts.forEach((host, hostIndex) => {
        if (!host.webApplications) {
          host.webApplications = [];
        }
        const box = el('div', 'host-box');
        const hostRow = el('div', 'zone-row');
        hostRow.append(
          bindText(textInput('', 'https://server:port'), host, 'hostUrl'),
          bindText(textInput('', 'Description'), host, 'description'),
          button('\u2715', () => {
            hosts.splice(hostIndex, 1);
            render();
          }, 'icon', 'Remove host')
        );
        box.appendChild(hostRow);
        host.webApplications.forEach((application, applicationIndex) => {
          const applicationRow = el('div', 'zone-row');
          applicationRow.style.paddingLeft = '20px';
          applicationRow.append(
            bindText(textInput('', 'Web application name'), application, 'name'),
            bindText(textInput('', 'Application base URL, e.g. /api'), application, 'applicationBaseUrl'),
            button('\u2715', () => {
              host.webApplications.splice(applicationIndex, 1);
              render();
            }, 'icon', 'Remove web application')
          );
          box.appendChild(applicationRow);
        });
        const addApplication = button('+ web application', () => {
          host.webApplications.push({ name: '', applicationBaseUrl: '' });
          render();
        });
        addApplication.style.marginLeft = '20px';
        box.appendChild(addApplication);
        container.appendChild(box);
      });
      setReadOnly(container, infrastructureIsReadOnly());
    };
    render();

    const addButton = button('+ Add host', () => {
      hosts.push({ hostUrl: '', description: '', webApplications: [] });
      render();
    });
    const save = saveButton('Save hosts', () => {
      callHost('saveInfrastructureHosts', { hosts: hosts }).then(applyInfrastructure).catch(showError);
    });
    addButton.disabled = infrastructureIsReadOnly();
    save.disabled = infrastructureIsReadOnly();
    pane.append(container, addButton, el('div', 'button-bar'), save);
    return pane;
  }

  function buildOAuthPane() {
    const configurations = deepCopy(ui.infrastructure.oauthConfigurations || []);
    const pane = el('div');
    pane.appendChild(el('div', 'info', 'OAuth configurations can be loaded within the "Issue\u2026" dialog of the authorization field.'));
    const container = el('div');
    const fields = [
      ['Name', 'name'],
      ['Authorize URL', 'authorizeUrl'],
      ['Token URL', 'tokenUrl'],
      ['Client id', 'clientId'],
      ['Client secret', 'clientSecret'],
      ['Redirect URI', 'redirectUri'],
      ['Scope', 'scope']
    ];

    const render = () => {
      container.innerHTML = '';
      configurations.forEach((configuration, index) => {
        const box = el('div', 'host-box');
        fields.forEach((field) => {
          const row = fieldRow(field[0]);
          row.appendChild(bindText(textInput(), configuration, field[1]));
          box.appendChild(row);
        });
        box.append(
          bindCheckbox('use PKCE (S256)', configuration, 'usePkce'),
          button('Remove', () => {
            configurations.splice(index, 1);
            render();
          })
        );
        container.appendChild(box);
      });
      setReadOnly(container, infrastructureIsReadOnly());
    };
    render();

    const addButton = button('+ Add configuration', () => {
      configurations.push({ name: '', authorizeUrl: '', tokenUrl: '', clientId: '', clientSecret: '', redirectUri: 'http://localhost/oauth-callback', scope: '', usePkce: true });
      render();
    });
    const save = saveButton('Save OAuth configurations', () => {
      callHost('saveOAuthConfigurations', { configurations: configurations }).then(applyInfrastructure).catch(showError);
    });
    addButton.disabled = infrastructureIsReadOnly();
    save.disabled = infrastructureIsReadOnly();
    pane.append(container, addButton, el('div', 'button-bar'), save);
    return pane;
  }

  function openSettingsDialog() {
    let closeDialog = null;
    const show = () => {
      const body = createTabs([
        { label: 'Client', pane: buildClientPane() },
        { label: 'Infrastructure source', pane: buildSourcePane(() => {
          closeDialog();
          show();
        }) },
        { label: 'Zones', pane: buildZonesPane() },
        { label: 'Hosts', pane: buildHostsPane() },
        { label: 'OAuth', pane: buildOAuthPane() }
      ]);
      closeDialog = openModal('Settings', body, [
        { label: 'Close', action: (close) => close() }
      ]);
    };
    loadInfrastructure().then(show);
  }

  // ======================================================================
  //  dialogs: issue token
  // ======================================================================

  function bindText(input, target, property) {
    input.value = target[property] || '';
    input.addEventListener('input', () => {
      target[property] = input.value;
    });
    return input;
  }

  function bindCheckbox(labelText, target, property) {
    const label = el('label');
    const checkbox = el('input');
    checkbox.type = 'checkbox';
    checkbox.checked = !!target[property];
    checkbox.addEventListener('change', () => {
      target[property] = checkbox.checked;
    });
    label.append(checkbox, document.createTextNode(' ' + labelText));
    return label;
  }

  function openIssueTokenDialog() {
    const authInput = ui.detail.elements.authInput;
    const jwt = Object.assign({}, ui.state.jwt);
    const oauth = Object.assign({}, ui.state.oauth);
    const body = el('div');

    // tabs
    const tabs = el('div', 'tabs');
    const jwtTab = el('div', 'tab active', 'JWT generator');
    const oauthTab = el('div', 'tab', 'OAuth code flow');
    tabs.append(jwtTab, oauthTab);
    const jwtPane = el('div');
    const oauthPane = el('div', 'hidden');
    jwtTab.addEventListener('click', () => {
      jwtTab.classList.add('active');
      oauthTab.classList.remove('active');
      jwtPane.classList.remove('hidden');
      oauthPane.classList.add('hidden');
    });
    oauthTab.addEventListener('click', () => {
      oauthTab.classList.add('active');
      jwtTab.classList.remove('active');
      oauthPane.classList.remove('hidden');
      jwtPane.classList.add('hidden');
    });

    // token output (shared)
    const tokenOutput = el('textarea', 'code');
    tokenOutput.rows = 4;
    tokenOutput.readOnly = true;
    const status = el('div', 'info');
    const applyButton = button('Apply as "Bearer <token>"', () => {
      authInput.value = 'Bearer ' + tokenOutput.value;
      closeDialog();
    }, 'primary');
    applyButton.disabled = true;

    const showToken = (token) => {
      tokenOutput.value = token;
      applyButton.disabled = !token;
      status.textContent = '';
    };

    // JWT pane
    const algorithmRow = fieldRow('Algorithm');
    const algorithmSelect = el('select');
    ['HS256', 'HS384', 'HS512', 'RS256', 'RS384', 'RS512', 'PS256', 'PS384', 'PS512', 'ES256', 'ES384', 'ES512', 'none'].forEach((name) => {
      const option = el('option', null, name);
      option.value = name;
      algorithmSelect.appendChild(option);
    });
    algorithmSelect.value = jwt.algorithm || 'HS256';
    algorithmSelect.addEventListener('change', () => {
      jwt.algorithm = algorithmSelect.value;
    });
    algorithmRow.appendChild(algorithmSelect);

    const signKeyInput = el('textarea', 'code');
    signKeyInput.rows = 3;
    signKeyInput.spellcheck = false;
    signKeyInput.placeholder = 'Shared secret (HS*) or PEM private key (RS*, PS*, ES*)';
    bindText(signKeyInput, jwt, 'signKey');

    const payloadInput = el('textarea', 'code');
    payloadInput.rows = 8;
    payloadInput.spellcheck = false;
    bindText(payloadInput, jwt, 'payloadJson');

    const lifetimeRow = fieldRow('Lifetime (min)');
    const lifetimeInput = el('input');
    lifetimeInput.type = 'number';
    lifetimeInput.min = '1';
    lifetimeInput.value = String(jwt.lifetimeMinutes);
    lifetimeInput.addEventListener('input', () => {
      jwt.lifetimeMinutes = parseInt(lifetimeInput.value, 10) || 0;
    });
    lifetimeRow.append(lifetimeInput, bindCheckbox('add iat / nbf / exp automatically', jwt, 'addTimestamps'));

    jwtPane.append(
      algorithmRow,
      el('div', 'info', 'Sign key'),
      signKeyInput,
      bindCheckbox('sign key is base64 encoded', jwt, 'signKeyIsBase64'),
      el('div', 'info', 'Payload (JSON)'),
      payloadInput,
      lifetimeRow,
      button('Generate', () => {
        callHost('generateJwt', { jwt: jwt }).then((token) => {
          ui.state.jwt = Object.assign({}, jwt);
          showToken(token);
        }).catch((error) => {
          status.textContent = error.message;
        });
      }, 'primary')
    );

    // OAuth pane
    const oauthFields = [
      ['Authorize URL', 'authorizeUrl'],
      ['Token URL', 'tokenUrl'],
      ['Client id', 'clientId'],
      ['Client secret', 'clientSecret'],
      ['Redirect URI', 'redirectUri'],
      ['Scope', 'scope']
    ];
    const oauthFieldInputs = {};
    const configurations = [];
    if (ui.infrastructure && ui.infrastructure.oauthConfigurations) {
      ui.infrastructure.oauthConfigurations.forEach((c) => configurations.push(c));
    }
    if (configurations.length > 0) {
      const configurationRow = fieldRow('Configuration');
      const configurationSelect = el('select');
      const none = el('option', null, '(manual input)');
      none.value = '';
      configurationSelect.appendChild(none);
      configurations.forEach((configuration, index) => {
        const option = el('option', null, configuration.name);
        option.value = String(index);
        configurationSelect.appendChild(option);
      });
      configurationSelect.addEventListener('change', () => {
        const index = parseInt(configurationSelect.value, 10);
        if (isNaN(index)) {
          return;
        }
        const configuration = configurations[index];
        Object.keys(oauthFieldInputs).forEach((property) => {
          oauth[property] = configuration[property] || '';
          oauthFieldInputs[property].value = oauth[property];
        });
        oauth.usePkce = !!configuration.usePkce;
        oauthPkceCheckbox.querySelector('input').checked = oauth.usePkce;
      });
      configurationRow.appendChild(configurationSelect);
      oauthPane.appendChild(configurationRow);
    }
    oauthFields.forEach((field) => {
      const row = fieldRow(field[0]);
      const input = bindText(textInput(), oauth, field[1]);
      oauthFieldInputs[field[1]] = input;
      row.appendChild(input);
      oauthPane.appendChild(row);
    });
    const oauthPkceCheckbox = bindCheckbox('use PKCE (S256)', oauth, 'usePkce');
    oauthPane.append(
      oauthPkceCheckbox,
      el('div', 'info', 'The login page is shown in an embedded browser window; the redirect URI is intercepted (it does not need to be reachable).'),
      button('Start code flow\u2026', () => {
        status.textContent = 'Waiting for the code flow\u2026';
        callHost('startOAuth', { oauth: oauth }).then((result) => {
          ui.state.oauth = Object.assign({}, oauth);
          if (result.cancelled) {
            status.textContent = 'Cancelled.';
          } else {
            showToken(result.token);
          }
        }).catch((error) => {
          status.textContent = error.message;
        });
      }, 'primary')
    );

    body.append(tabs, jwtPane, oauthPane, el('div', 'info', 'Issued token'), tokenOutput, status, applyButton);

    const closeDialog = openModal('Issue authorization token', body, [
      { label: 'Close', action: (close) => close() }
    ]);
  }

  // ======================================================================
  //  initialization
  // ======================================================================

  function initSplitter() {
    const splitter = document.getElementById('splitter');
    const leftPanel = document.getElementById('leftPanel');
    let dragging = false;

    splitter.addEventListener('mousedown', (event) => {
      event.preventDefault();
      dragging = true;
      document.body.classList.add('resizing');
    });
    document.addEventListener('mousemove', (event) => {
      if (!dragging) {
        return;
      }
      let width = event.clientX;
      if (width < 200) {
        width = 200;
      }
      if (width > window.innerWidth - 300) {
        width = window.innerWidth - 300;
      }
      leftPanel.style.width = width + 'px';
    });
    document.addEventListener('mouseup', () => {
      if (!dragging) {
        return;
      }
      dragging = false;
      document.body.classList.remove('resizing');
      callHost('setLayout', { leftPanelWidth: Math.round(leftPanel.getBoundingClientRect().width) }).catch(showError);
    });
  }

  function initDragAndDrop() {
    const containsFiles = (event) => {
      return event.dataTransfer && Array.from(event.dataTransfer.types).indexOf('Files') >= 0;
    };
    document.addEventListener('dragover', (event) => {
      if (containsFiles(event)) {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
      }
    });
    document.addEventListener('drop', (event) => {
      if (!containsFiles(event)) {
        return;
      }
      event.preventDefault();
      const files = Array.from(event.dataTransfer.files);
      if (files.length === 0) {
        return;
      }
      callHostWithFiles('dropFiles', files).then(applyState).catch(showError);
    });
  }

  function initKeyboard() {
    document.getElementById('mainTree').addEventListener('keydown', (event) => {
      if (event.key !== 'Delete' || !ui.selectedKey) {
        return;
      }
      const parts = JSON.parse(ui.selectedKey);
      if (parts[0] !== 'dll') {
        return;
      }
      event.preventDefault();
      callHost('removeDll', { path: parts[1] }).then((state) => {
        ui.selectedKey = null;
        ui.detail = null;
        showDetailPlaceholder();
        applyState(state);
      }).catch(showError);
    });

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        hideContextMenu();
      }
    });
    document.addEventListener('mousedown', (event) => {
      if (!document.getElementById('contextMenu').contains(event.target)) {
        hideContextMenu();
      }
      document.querySelectorAll('.combo-list').forEach((list) => {
        if (!list.parentElement.contains(event.target)) {
          list.classList.add('hidden');
        }
      });
    });
    window.addEventListener('blur', hideContextMenu);
  }

  function init() {
    document.getElementById('btnAddDll').addEventListener('click', () => {
      callHost('addDllsViaDialog').then(applyState).catch(showError);
    });
    document.getElementById('btnSettings').addEventListener('click', () => {
      if (!isModalOpen()) {
        openSettingsDialog();
      }
    });
    initSplitter();
    initDragAndDrop();
    initKeyboard();

    callHost('getState').then((state) => {
      document.getElementById('leftPanel').style.width = state.leftPanelWidth + 'px';
      applyState(state);
      return loadInfrastructure();
    }).catch(showError);
  }

  init();

})();
