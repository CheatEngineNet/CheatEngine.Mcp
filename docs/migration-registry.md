# Registre de migration des sources antérieures

Ce registre est la preuve de reprise avant le retrait des anciens répertoires.
Il sépare ce qui est déjà porté, ce qui reste à réaliser dans le contrat v2 et ce qui est exclu.
Les noms d'outils historiques ne sont jamais publiés comme alias : la table contrôlée par les tests est `tests/CheatEngine.Mcp.Tests/Contract/Golden/legacy-tool-map.txt`.
Au relevé de clôture, les 172 méthodes backend v2 sont implémentées et les 191 lignes de cette table couvrent les 117 noms publiés par la Copie.

## Instantané et périmètre

L'inventaire a été relevé le 2026-09-27 (Europe/Paris), avant toute suppression.
Les empreintes de contenu sont SHA-256 en minuscules; les identifiants Git sont les objets de l'index SHA-1 du dépôt source.
`docs/migration/legacy-copy-head.csv` est le manifeste complet des 64 chemins suivis à `HEAD`, avec leur état dans l'index ou l'arbre de travail et leur empreinte de contenu historique.
`docs/migration/legacy-copy-untracked.sha256` inventorie les 114 fichiers non suivis, avec une empreinte SHA-256 par fichier.
`docs/migration/legacy-copy-untracked-disposition.csv` partitionne exactement ces 114 chemins en groupes non chevauchants et consigne, pour chacun, sa destination v2, son test ou son exclusion.
`docs/migration/legacy-copy-ignored.sha256` et `legacy-copy-ignored-disposition.md` consignent les 1 155 fichiers ignorés de la première source, dont 1 153 sorties générées et deux métadonnées IDE, sans recopier leur contenu.
`docs/migration/ce-mcp-head.csv`, `ce-mcp-index.csv` et `ce-mcp-worktree.csv` séparent respectivement les 113 chemins à `HEAD`, les 173 chemins de l'index et les 103 différences entre index et arbre de travail du second dépôt.
`docs/migration/ce-mcp-untracked.sha256` porte les empreintes des huit fichiers non suivis du second dépôt.
`docs/migration/ce-mcp-ignored.sha256` porte les empreintes des 998 fichiers ignorés du second dépôt, sans en recopier le contenu.
`docs/migration/ce-mcp-disposition.csv` couvre une fois chacun de ses 181 chemins suivis à `HEAD`, ajoutés dans l'index ou non suivis, avec une destination v2 ou une exclusion motivée.

| Source | État relevé | Révision et branches | Décision |
|---|---|---|---|
| `D:\\CheatEngine\\CheatEngine.Mcp - Copie` | Dépôt Git, 64 fichiers à `HEAD`; 50 suppressions indexées, une modification non indexée (`global.json`) et 114 fichiers non suivis. | `HEAD 4930a453f5ad36c06995eaf7436d04138c8924cd`, branche `base-update`; branches locales `base-update`, `main`; `origin/main` présent. | Source à conserver jusqu'à la clôture des entrées ci-dessous. |
| `C:\\Users\\Arius\\RiderProjects\\ce-mcp` | Dépôt Git, 113 chemins à `HEAD`, 60 ajouts dans l'index, 103 différences index-arbre et huit fichiers non suivis. | `HEAD 8862293a7d0865c7944d23536342d0f6411ff9c5`, branche `base-update`, `origin https://github.com/AriusII/ce-mcp.git`. | Source à conserver jusqu'à la clôture des entrées et contrôles ci-dessous. |

Le sous-module historique `CESDK` est un gitlink vers `b0b1c7491b3dfb527134c42e94268ae0fa6905ab`.
Il n'est pas copié : v2 utilise `CheatEngine.Client` dans Core et garde la référence SDK directe seulement dans Plugin, comme l'imposent les règles d'architecture.

## État Git de la première source

Les suppressions sont indexées, donc doivent être lues avec `git diff --cached`, et non seulement avec `git diff`.

| Catégorie | Nombre | Inventaire ou preuve |
|---|---:|---|
| Fichiers suivis à `HEAD` | 64 | `docs/migration/legacy-copy-head.csv` |
| Suppressions indexées | 50 | Tous les anciens `src/`, la solution et le projet historiques, le sous-module CESDK et les tests `CeMCP.Tests`; l'état est `staged-deleted` dans le manifeste. |
| Modification non indexée | 1 | `global.json`: SDK `10.0.102` / MSTest `4.2.3` vers SDK `10.0.401` / MSTest `4.4.1`; voir son empreinte historique et celle de l'arbre de travail dans le manifeste. |
| Fichiers suivis restants | 14 | Workflows, licence, README, skill et fichiers d'environnement; leurs empreintes sont aussi consignées. |
| Fichiers non suivis | 114 | `docs/migration/legacy-copy-untracked.sha256` et `docs/migration/legacy-copy-untracked-disposition.csv`; ils constituent l'ébauche v2 de Core, Bridge, Tools, Prompts, Resources, Plugin et tests. |
| État local ignoré | 1 155 fichiers | `docs/migration/legacy-copy-ignored.sha256` et `legacy-copy-ignored-disposition.md`; sorties générées et métadonnées IDE exclues du produit. |

Les commandes de contrôle, à exécuter depuis le dépôt cible sans modifier la source, sont :

```powershell
git -C 'D:\CheatEngine\CheatEngine.Mcp - Copie' status --porcelain=v2 --branch
git -C 'D:\CheatEngine\CheatEngine.Mcp - Copie' diff --cached --name-status
git -C 'D:\CheatEngine\CheatEngine.Mcp - Copie' diff --name-status
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:\CheatEngine\CheatEngine.Mcp - Copie\<chemin>'
```

Une divergence de `HEAD`, des statuts ou d'une empreinte impose de refaire l'inventaire avant de supprimer la source.

## État Git de la seconde source

Le second dépôt est `C:\Users\Arius\RiderProjects\ce-mcp`.
Son `HEAD` est `8862293a7d0865c7944d23536342d0f6411ff9c5` sur la branche `base-update`, avec le message `Refonte architecture: multi-project solution with DI-first design` daté du 2026-09-17T16:28:15+02:00.
Son état est une refonte interrompue et doit être interprété par rapport à chacun des trois arbres Git, sans confondre les ajouts indexés avec les modifications de l'arbre de travail.

| État | Nombre | Manifeste et décision |
|---|---:|---|
| Arbre `HEAD` | 113 chemins | `docs/migration/ce-mcp-head.csv` consigne le blob Git et l'empreinte SHA-256 de chaque contenu exact. |
| Index | 173 chemins | `docs/migration/ce-mcp-index.csv` consigne les 113 chemins inchangés de `HEAD` et les 60 ajouts indexés. |
| Différence index-arbre | 103 chemins | `docs/migration/ce-mcp-worktree.csv` sépare 70 suppressions, 22 modifications de chemins suivis et 11 modifications d'ajouts indexés. |
| Non suivis | 8 fichiers | `docs/migration/ce-mcp-untracked.sha256` porte les huit empreintes; `ce-mcp-disposition.csv` leur donne une destination ou exclusion. |
| Disposition | 181 chemins uniques | `docs/migration/ce-mcp-disposition.csv` couvre les 113 chemins `HEAD`, les 60 ajouts indexés et les huit non suivis. |
| État local ignoré | 19 entrées Git ignorées, 998 fichiers | `.claude/` contient deux réglages locaux, `.idea/` quatre métadonnées IDE, `bin/` et `obj/` 991 sorties générées, et `FodyWeavers.xsd` un schéma généré; `ce-mcp-ignored.sha256` porte leurs empreintes sans en exposer le contenu. |
| Reparse points à la racine | 0 | Aucune traversée de lien de système de fichiers n'est nécessaire pour la suppression future. |

Les 60 chemins `LEGACY/` du `HEAD` de cette source ont le même blob Git que les chemins correspondants de `D:\CheatEngine\CheatEngine.Mcp - Copie` sans le préfixe `LEGACY/`.
Ils sont donc déjà couverts par les manifestes et la correspondance de la première source, sans les recopier dans le produit v2.
Les 117 noms MCP publiés par les outils ajoutés à l'index appartiennent tous à `legacy-tool-map.txt`, où ils ont une destination v2 ou une exclusion motivée.
La source comporte aussi une interface WPF, un serveur HTTP direct, une couche `CESDK.dll` et une réflexion d'enregistrement des outils; ces éléments sont explicitement exclus par l'architecture v2 et leur remplacement est consigné pour chaque chemin dans la disposition.
La lecture des 103 différences index-arbre ne révèle aucune capacité fonctionnelle unique : les 70 suppressions retirent la copie `LEGACY/`, le SDK binaire, l'ancien hôte et l'interface WPF; les modifications de code ajoutent l'enregistrement des 117 outils historiques, leur DI et le conditionnement de la skill, déjà remplacés par les builders v2 explicites, le gateway et le bundle `cheatengine-mcp`.
Les changements restants sont des commentaires, noms d'espaces, documentation, métadonnées de build ou tests de l'architecture intermédiaire; ils ne demandent aucune extension du catalogue v2.

Les commandes de contrôle, à exécuter depuis le dépôt cible sans modifier la source, sont :

```powershell
git -C 'C:\Users\Arius\RiderProjects\ce-mcp' status --porcelain=v1 -uall
git -C 'C:\Users\Arius\RiderProjects\ce-mcp' diff --cached --name-status
git -C 'C:\Users\Arius\RiderProjects\ce-mcp' diff --name-status
Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\Arius\RiderProjects\ce-mcp\<chemin>'
```

Une divergence de `HEAD`, de la composition des états, ou d'une empreinte impose de régénérer les cinq manifestes de cette source avant sa suppression.

## Correspondance des capacités historiques

Les chemins historiques dans ce tableau sont absents de l'arbre de travail de la source mais récupérables exactement par `git show 4930a45:<chemin>`; leurs SHA-256 sont dans le manifeste.
La colonne « Preuve » désigne la couverture v2 ou l'endroit précis où elle doit être ajoutée.

| Source historique | Capacités historiques | Destination v2 ou exclusion | Preuve |
|---|---|---|---|
| `src/Tools/AddressListTool.cs` | Liste, création, mise à jour et suppression d'enregistrements, effacement de table | `record_list`, `record_get`, `record_find`, `record_create`, `record_update`, `record_delete`, `record_clear`; l'effacement renvoie le nombre supprimé. | `srcs/CheatEngine.Mcp.Tools/Record/`; `tests/CheatEngine.Mcp.Tests/Tools/Record/RecordToolsTests.cs`, `RecordClearToolsTests.cs` et `NativeLua/NativeLuaRecordClearTests.cs`. |
| `src/Tools/AdvancedMemoryTool.cs` | Allocation, libération, protection, copie, comparaison, hash, dump et chargement fichier | `memory_allocate`, `memory_free`, `memory_set_protection`, `memory_copy`, `memory_compare`, `memory_hash`, `memory_dump_to_file`, `memory_load_from_file`. Le chargement v2 applique le chemin autorisé, écrit en blocs et signale un effet partiel. | `srcs/CheatEngine.Mcp.Tools/Memory/MemoryFileTools.cs`; `tests/CheatEngine.Mcp.Tests/Tools/Memory/MemoryFileToolsTests.cs`. `allocate_shared_memory` est exclu jusqu'à une libération de mapping vérifiable. |
| `src/Tools/AnalysisTool.cs`, `MemoryViewTool.cs` | Analyse de code, recherche de désassemblage, références, chaînes, fonctions, commentaires et régions mémoire | `code_start_dissect`, `code_start_search`, `code_poll_job`, `code_find_references`, `code_find_strings`, `code_list_functions`, `code_get_comments`, `code_set_comment`, `code_*disassemble*`, `memory_list_regions`. Les opérations longues passent par jobs. | `srcs/CheatEngine.Mcp.Tools/Code/`; `tests/CheatEngine.Mcp.Tests/Tools/CodeAsmTableV2Tests.cs` et contrats de jobs Core. |
| `src/Tools/AssemblyTool.cs`, `AutoAssemblyTool.cs` | Désassemblage, résolution, assemblage et Auto Assembler | `code_disassemble`, `code_decode`, `asm_assemble`, `asm_check`, `asm_apply`, `asm_apply_code_patch`, `asm_release_patch`, `asm_list_patches`, `asm_generate_injection`, `asm_generate_api_hook`. | `srcs/CheatEngine.Mcp.Tools/Asm/`; `tests/CheatEngine.Mcp.Tests/Tools/CodeAsmTableV2Tests.cs` et `Core/AutoAssemblerScriptClassifierTests.cs`. |
| `src/Tools/CheatTableTool.cs` | Chargement et sauvegarde de tables | `table_load`, `table_save`, `table_list_files`, avec contrôle de chemin. | `srcs/CheatEngine.Mcp.Tools/Table/`; `tests/CheatEngine.Mcp.Tests/Tools/CodeAsmTableV2Tests.cs`. |
| `src/Tools/ConversionTool.cs`, `AddressParser.cs` | Conversion de chaînes et calcul/résolution | `util_convert_value`, `util_calculate`, `symbol_resolve`; les adresses relèvent de `HexParse` et `McpValueCodec`. | `libs/CheatEngine.Mcp.Core/Values/`; `tests/CheatEngine.Mcp.Tests/Core/HexValuesTests.cs`. |
| `src/Tools/DbvmTool.cs` | DBVM, mémoire physique et watch | `kernel_get_status`, `kernel_initialize_dbvm`, `kernel_translate_address`, `kernel_read_physical`, `kernel_write_physical`, `kernel_start_watch`, `kernel_poll_watch`, derrière la capacité kernel. | `srcs/CheatEngine.Mcp.Tools/Kernel/`; `tests/CheatEngine.Mcp.Tests/Tools/Kernel/KernelToolsTests.cs`. |
| `src/Tools/DebuggerTool.cs` | Attache, points d'arrêt, contexte, exécution, capture, trace et pile | Famille `debugger_*`: attache/détache, statut, breakpoints, contexte, pas, exécution, capture et trace. Les contextes XMM font partie de `debugger_get_context`; les API LBR historiques sont exclues, car la documentation Lua CE les décrit comme peu fiables sous Windows moderne. | `srcs/CheatEngine.Mcp.Tools/Debugger/`; `tests/CheatEngine.Mcp.Tests/Tools/Debugger/DebuggerToolsTests.cs`. |
| `src/Tools/InjectionTool.cs` | Injection native/.NET, appels distant/local, génération de hook et compilation C | `exec_inject_library`, `exec_inject_dotnet`, `exec_call_remote`, `exec_call_method`, `exec_call_local`, `exec_compile_c`, plus `asm_generate_*`; capacité d'exécution requise. | `srcs/CheatEngine.Mcp.Tools/Exec/`; `tests/CheatEngine.Mcp.Tests/Tools/Exec/ExecToolsTests.cs`. |
| `src/Tools/LuaExecutionTool.cs` | Lua libre | `lua_execute` et `lua_find_api`; passage obligé par le runtime Lua v2 borné et la capacité `UnsafeLua`. | `srcs/CheatEngine.Mcp.Tools/Lua/`; `tests/CheatEngine.Mcp.Tests/Tools/Lua/LuaToolsTests.cs` et `NativeLuaUnsafeScriptTests.cs`. |
| `src/Tools/MemoryTool.cs` | Lecture/écriture typée de mémoire | `memory_read`, `memory_read_batch`, `memory_write`, `memory_write_batch`, `memory_get_address_info`. | `srcs/CheatEngine.Mcp.Tools/Memory/`; `MemoryReadToolsTests.cs`, `MemoryWriteToolsTests.cs`, `MemoryPipelineTests.cs`. |
| `src/Tools/PointerTool.cs` | Chaînes et références de pointeurs | `pointer_read_chain`, `pointer_find_references`, cartes, scans, chemins et rescans `pointer_*`. | `srcs/CheatEngine.Mcp.Tools/Pointer/`; `tests/CheatEngine.Mcp.Tests/Tools/Pointer/`. |
| `src/Tools/ProcessControlTool.cs`, `ProcessTool.cs` | Liste et attache de processus, création, pause, threads, taille de pointeur et fichiers | `process_list`, `process_attach`, `process_get_current`, `process_create`, `process_set_paused`, `process_list_threads`, `process_set_pointer_size`, `process_open_file`, `process_save_file`. `open_foreground_process` est exclu: la sélection explicite par PID protège les instances multiples et évite une cible variable. | `srcs/CheatEngine.Mcp.Tools/Processes/`; `tests/CheatEngine.Mcp.Tests/Tools/RuntimeProcessScanV2ContractTests.cs`, `NativeLua/NativeLuaRuntimeProcessScanV2Tests.cs`; vérification d'identité par le gateway. |
| `src/Tools/ScanTool.cs` | AOB, AOB de module, chaînes et scans mémoire | `aob_find`, `aob_generate_signature`, `scan_first`, `scan_next`, `scan_get_status`, `scan_list_results`, `scan_list_scanners`, `scan_reset`, `scan_delete`, `scan_stop`; les scans de module passent par la plage du module v2. | `srcs/CheatEngine.Mcp.Tools/Aob/` et `srcs/CheatEngine.Mcp.Tools/Scan/`; `Aob/AobToolsTests.cs`, `Scan/ScanToolsV2Tests.cs`, `NativeLua/NativeLuaRuntimeProcessScanV2Tests.cs`. |
| `src/Tools/StructureTool.cs` | Structures globales, éléments, autoguess, comparaison | Famille `structure_*`, y compris .NET, PDB, lecture et écriture d'élément. | `srcs/CheatEngine.Mcp.Tools/Structures/`; tests `Tools/Structures/` et `NativeLua/NativeLuaStructureTests.cs`. |
| `src/Tools/SymbolRegistryTool.cs`, `SymbolTool.cs` | Modules, RTTI, résolution, registre et sources de symboles | `module_list`, `module_get`, `module_list_exports`, `module_find_patches`, `symbol_resolve`, `symbol_register`, `symbol_unregister`, `symbol_list_registered`, `symbol_reload`, `symbol_add_module`, `symbol_enable_sources`. Les sources Windows indiquent l'accès réseau externe; les sources kernel exigent `KernelAccess`. | `srcs/CheatEngine.Mcp.Tools/Symbol/`; `SymbolToolTests.cs`, `SymbolRegistrationToolTests.cs`, `ModuleSymbolPipelineTests.cs`. |
| `src/Tools/ToolThread.cs` | Sérialisation du travail CE sur le thread principal et forme d'erreur ad hoc | `ToolDispatch`, erreurs typées `CheatEngineToolException`, limites, feature gates, registres de jobs et ressources. | `libs/CheatEngine.Mcp.Core/Execution/ToolDispatch.cs`; `ToolDispatchTests.cs`, `ToolFailureMappingTests.cs`, `TargetResourcesTests.cs`, `JobRegistryTests.cs`. |

## Écarts historiques désormais repris dans la table gelée

La Copie publie 117 noms historiques. `legacy-tool-map.txt` contient désormais 191 lignes et couvre ces 117 noms sans écart.
Les 49 entrées ci-dessous étaient absentes de la version à 142 lignes, principalement parce qu'elle contenait une autre famille historique `debugger_*` plutôt que `dbg_*`; elles sont maintenant reportées dans le golden file et protégées par les tests de contrat.

| Noms historiques de la Copie | Destination v2 ou exclusion précise |
|---|---|
| `allocate_shared_memory` | Exclu : aucun cycle de libération vérifiable du mapping CE n'est établi; ne pas le confondre avec `memory_allocate`. |
| `aob_scan_module_unique` | `aob_find`, avec filtre de module et unicité par motif. |
| `clear_address_list` | `record_clear`, effacement hiérarchique avec nombre supprimé. |
| `compare_structures` | `structure_compare`. |
| `compile_c` | `exec_compile_c`, avec capacité d'exécution. |
| `dbg_add_bp` | `debugger_set_breakpoint` pour un arrêt; `debugger_start_capture` puis `debugger_poll_capture` pour l'ancien suivi de hits. |
| `dbg_add_thread_bp` | `debugger_set_breakpoint`, avec options de méthode, thread et one-shot. |
| `dbg_bps` | `debugger_list_breakpoints`. |
| `dbg_break_thread` | `debugger_break_thread`. |
| `dbg_clear_bp_hits` | Workflow de remplacement : `runtime_stop_job` détruit la capture et ses hits, puis `debugger_start_capture` recrée la collecte. Aucun effacement in situ, car les polls ne consomment pas les hits. |
| `dbg_context_table` | `debugger_get_context`, FPU/XMM inclus sur demande. |
| `dbg_continue` | `debugger_continue`; `debugger_step` couvre les modes step. |
| `dbg_delete_bp` | `debugger_delete_breakpoint`. |
| `dbg_exclude_thread` | `debugger_set_thread_ignored(ignored=true)`. |
| `dbg_exit` | `debugger_detach`. |
| `dbg_get_bp_hits` | `debugger_poll_capture`, après `debugger_start_capture`. |
| `dbg_gpregs`, `dbg_gpregs_remote`, `dbg_regs`, `dbg_regs_all`, `dbg_regs_named`, `dbg_regs_named_remote`, `dbg_regs_remote` | `debugger_get_context`, réponse typée avec sélection des registres et contexte d'arrêt. |
| `dbg_include_thread` | `debugger_set_thread_ignored(ignored=false)`. |
| `dbg_is_broken`, `dbg_is_debugging` | `debugger_get_status`. |
| `dbg_lbr_enable`, `dbg_lbr_records` | Exclu : LBR CE/Lua est peu fiable sous Windows moderne. |
| `dbg_read` | `memory_read`; l'outil historique lisait la mémoire, pas les registres. |
| `dbg_read_xmm` | `debugger_get_context(includeExtraRegisters=true)`. |
| `dbg_run_to` | `debugger_run_to`. |
| `dbg_stacktrace` | `debugger_get_stack_trace`. |
| `dbg_start` | `debugger_attach`. |
| `dbg_step_into`, `dbg_step_over` | `debugger_step`, avec le mode approprié. |
| `dbg_toggle_bp` | Scindé : `debugger_list_breakpoints`, puis `debugger_set_breakpoint` ou `debugger_delete_breakpoint`; le toggle implicite disparaît. |
| `dbg_write` | `memory_write`; l'outil historique écrivait la mémoire, pas les registres. |
| `enable_symbols` | `symbol_enable_sources`; Windows rend l'accès externe explicite et kernel exige `KernelAccess`. |
| `execute_remote_function`, `execute_remote_function_ex` | `exec_call_remote`, arguments typés, convention d'appel et limites. |
| `find_pointer_references` | `pointer_find_references`. |
| `generate_code_injection_script` | `asm_generate_injection`. |
| `get_module_size` | `module_get`, qui retourne base, taille, PE et sections. |
| `get_rtti_class_name` | `memory_get_address_info(includeRtti=true)`. |
| `inject_dotnet_assembly` | `exec_inject_dotnet`. |
| `load_memory` | `memory_load_from_file`, chemin autorisé, blocs et effet partiel explicite. |
| `open_foreground_process` | Exclu : la fenêtre au premier plan est variable; v2 impose `process_attach` explicite par PID ou nom exact pour préserver l'isolation multi-instance. |
| `search_disassembly` | `code_start_search(mode="text")`, puis `code_poll_job`; résultat borné et arrêt par `runtime_stop_job`. |
| `string_scan` | `scan_first` avec type chaîne, puis `scan_get_status` et `scan_list_results` sur un scanner nommé. |

## Hôte, transport et interface exclus

| Source historique | Décision motivée | Destination ou preuve |
|---|---|---|
| `src/McpServer.cs` | Exclure le serveur HTTP direct et son endpoint streamable sans authentification d'instance. | Backend local authentifié et gateway stdio multi-instance; `tests/CheatEngine.Mcp.Tests/Hosting/GatewayServerTests.cs`, `GatewayTokenLeakTests.cs`, `InstanceRegistryTests.cs`. |
| `src/Plugin.cs`, `src/Models/ConfigurationModel.cs`, `src/ServerConfig.cs`, `src/ThemeHelper.cs` | Reprendre le cycle de vie et la configuration, sans l'ancien état statique. | `srcs/CheatEngine.Mcp.Plugin/`, `srcs/CheatEngine.Mcp.Hosting/Configuration/` et tests `Plugin/McpModuleLifecycleTests.cs`, `Plugin/McpConfigurationTests.cs`. |
| `src/Views/ConfigWindow.xaml`, `src/Views/ConfigWindow.xaml.cs` | Exclure l'interface WPF de configuration; le produit distribue une configuration fichier/environnement et le gateway stdio. | `srcs/CheatEngine.Mcp.Plugin/McpPluginConfigurationBuilderExtensions.cs` et `srcs/CheatEngine.Mcp.Hosting/Configuration/`; aucune reprise d'UI. |
| `.gitmodules`, `CESDK`, `CeMCP.csproj`, `CeMCP.sln`, `FodyWeavers.xml` | Exclure la topologie plugin monolithique, Costura et la dépendance CESDK transitive historique. | Solution v2 et règles CEMCP002/CEMCP003/CEMCP006; `ArchitectureTests.cs`. |

## Documentation et tests historiques

| Élément historique | Décision | Preuve v2 |
|---|---|---|
| `README.md`, `skills/ce-mcp/**` | Repris dans le README, `docs/` et `skills/cheatengine-mcp/`; les noms sortants doivent provenir du catalogue v2. | `SkillCatalogTests.cs`, `ToolCatalogContractTests.cs`, `ServerInstructionsTests.cs`. |
| `AGENTS.md` | Les conventions de transport HTTP/WPF/CESDK sont exclues; les règles v2 maintenues doivent coïncider avec le code. | `AGENTS.md`, `docs/development.md`, `ArchitectureTests.cs`. |
| `.github/workflows/**`, `.vscode/settings.json`, `.gitignore`, `global.json`, `LICENSE` | Configuration ou licence, revue séparément; aucun comportement d'outil ne doit être perdu. Le seul diff non indexé de la source est consigné ci-dessus. | `Directory.Build.*`, lock files et tests de packaging/contrat. |
| `tests/CeMCP.Tests/**` | Les assertions liées au serveur HTTP et à CESDK ne sont pas copiées; leurs comportements sont couverts par les tests portables v2, NativeLua et la qualification live séparée. | `tests/CheatEngine.Mcp.Tests/Contract/`, `NativeLua/`, `LiveQualification/`. |

## Condition de retrait

Ne supprimer aucun ancien chemin tant que l'un des deux dépôts n'est pas entièrement inventorié, qu'une ligne ci-dessus est « à réaliser », ou qu'une empreinte ne correspond plus.
La décision de retrait exige aussi un build Release sans avertissement, les tests portables et NativeLua applicables, les golden files du gateway/backend et une publication Native AOT vérifiée hors Cheat Engine.
La qualification dans CE 7.7 x64 est suivie séparément et n'est pas la condition de suppression retenue par cette migration.

## Relevé de validation antérieur

Le contrôle du 2026-09-27 confirme que la première source est toujours à 4930a453f5ad36c06995eaf7436d04138c8924cd : 64 chemins suivis à HEAD, 50 suppressions indexées, une modification non indexée, 114 fichiers non suivis et zéro divergence d'empreinte SHA-256 sur ces 114 fichiers.
Le relevé antérieur passe en restauration verrouillée et build sans avertissement ni erreur; il compte 1 771/1 771 tests portables et 199/199 tests NativeLua.
Le relevé antérieur du profil Native AOT utilise son verrou distinct `srcs/CheatEngine.Mcp.Gateway/packages.aot.lock.json`; sa restauration verrouillée et la publication Release passent, avec un smoke MCP du gateway hors Cheat Engine.
Les contrôles antérieurs de style, espaces, liens Markdown et catalogues de contrat passent.
Ces résultats étaient une base de comparaison avant les derniers changements de contrat, les manifestes et les corrections documentaires.

## Validation du relevé antérieur et état du retrait

Le 2026-09-27, la restauration verrouillée et le build de la solution passent, avec zéro avertissement et zéro erreur.
Les tests portables passent à 1 728/1 728 sans saut, et les tests NativeLua passent à 167/167 sans saut avec le Lua 5.3 x64 de Cheat Engine.
Les contrôles `dotnet format style`, `dotnet format whitespace` et `git diff --check` passent.
Le build Release passe avec zéro avertissement et zéro erreur.
`eng/Publish.ps1 -Configuration Release` produit les 26 fichiers du Plugin et le gateway Native AOT win-x64; son smoke MCP hors Cheat Engine passe pour l'initialisation, les outils, les ressources et les prompts.
Le contrôle des 75 fichiers Markdown trouve 464 liens relatifs valides et aucun lien manquant.

Immédiatement avant les tentatives de retrait, les deux chemins exacts ont été résolus et contrôlés comme des répertoires sans reparse point, y compris dans leurs descendants.
La première source reste à `HEAD 4930a453f5ad36c06995eaf7436d04138c8924cd`; ses 64 chemins HEAD, 14 entrées d'index, 50 suppressions indexées, 114 fichiers non suivis et 1 155 fichiers ignorés correspondent aux manifestes, empreintes SHA-256 comprises.
La seconde reste à `HEAD 8862293a7d0865c7944d23536342d0f6411ff9c5`; ses 113 chemins HEAD, 173 entrées d'index, 103 différences index-arbre, huit fichiers non suivis et 998 fichiers ignorés correspondent également aux manifestes et empreintes.
Les deux commandes de suppression, d'abord groupées puis chacune avec son chemin littéral, ont été rejetées avant exécution par le contrôle automatique avec la seule raison « blocked by policy ».
Les deux anciens dossiers étaient donc toujours présents à l'issue de cette tentative; le retrait demandé restait à effectuer par un opérateur disposant de cette autorisation dans son environnement.
La qualification dans Cheat Engine 7.7 x64 reste un contrôle manuel distinct et n'a pas été exécutée pendant cette passe.

## Observation ultérieure des chemins sources

Le 2026-09-27, une vérification en lecture seule de `D:\CheatEngine\CheatEngine.Mcp - Copie` et de `C:\Users\Arius\RiderProjects\ce-mcp` avec `Test-Path -LiteralPath` renvoie `False` pour les deux chemins exacts.
Leur disparition a eu lieu en dehors des commandes de suppression rejetées ci-dessus; ce relevé ne l'attribue pas à l'agent et ne démontre pas comment les chemins ont été retirés.
Les manifestes de migration et les empreintes précédant le retrait restent dans ce dépôt pour préserver la traçabilité des sources.

## Validation après consolidation sur `feat/archi`

Le 2026-09-27, la restauration verrouillée et le build de la solution passent avec zéro avertissement et zéro erreur.
Les golden files backend et gateway ont été régénérés pour le catalogue v2 puis vérifiés sans la variable de mise à jour.
La suite portable passe avec 1 757 tests réussis et aucun échec ni saut; la suite NativeLua passe avec 176 tests réussis et aucun échec ni saut avec `lua53-64.dll` x64.
`dotnet format style`, `dotnet format whitespace` et `git diff --check` passent après les corrections finales.
Le build Release déploie le Plugin avec zéro avertissement et zéro erreur; le dossier distribué contient sa DLL, ses dépendances et ses fichiers de configuration.
La publication Native AOT Windows x64 du gateway passe avec l'environnement C++ Visual Studio et son smoke MCP hors Cheat Engine valide `initialize`, `tools/list`, `resources/list`, `resources/templates/list` et `prompts/list`.
Une nouvelle vérification en lecture seule des deux anciens chemins exacts renvoie `False` pour chacun; aucune commande de suppression n'a été exécutée pendant cette consolidation.
La qualification dans Cheat Engine 7.7 x64 reste distincte et n'a pas été exécutée.
