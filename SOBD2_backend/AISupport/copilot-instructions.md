# Contexto do Projeto: Telemetria OBD-II em Tempo Real (TCC / Projeto de ADS)

> Este arquivo é a memória do projeto. Leia tudo antes de sugerir código.
> Idioma das respostas: **português do Brasil**, linguagem simples e didática.

---

## 1. Quem sou eu e como quero ser ajudado

- Sei **C# razoavelmente**, sei **pouco de C/C++**.
- Já tenho um sistema de telemetria com WebSocket/SignalR em C#: repositório `https://github.com/Gabriel-Te/ACRH` (Clean Architecture, Hub, DTOs). Posso reaproveitar a estrutura, **exceto** a leitura de Shared Memory com `unsafe`.
- Preciso **entender e defender** cada parte do código (apresentação para banca). Por isso:
  - **Explique o porquê** de cada decisão, em termos simples.
  - **Não escreva sozinho a lógica central** sem eu pedir. Prefira me guiar, revisar e corrigir o que eu escrevi. A lógica central é: `PidDecoder`, `LatestValueStore`, `IChartStrategy`/`ChartStrategyBase` e o loop de leitura do firmware.
  - **Pode gerar à vontade o boilerplate:** configuração do .NET, endpoint WebSocket, SignalR, CSS, tabela de PIDs, parsers repetitivos.
  - Dê passos pequenos. Uma fatia que funciona de ponta a ponta vale mais que várias peças soltas.

---

## 2. Objetivo do sistema

Telemetria automotiva em tempo real, **não exclusiva de um carro** (o teste será um Chevrolet Celta). Deve funcionar com qualquer veículo CAN que responda ao **OBD-II padrão (SAE J1979, Mode 01)**.

Arquitetura distribuída em 3 camadas, com **decisão centralizada no backend**:

```
CARRO (ECU)
   ↕  CAN / OBD-II
ELM327 Bluetooth (tradutor)
   ↕  Bluetooth Clássico (SPP)
ESP32 (mensageiro "burro", firmware C++)
   ↕  WebSocket puro   ws://IP:5000/ws/device
BACKEND C# / .NET (cérebro, TelemetryHub)
   ↕  SignalR          /hubs/telemetry
NAVEGADOR (JavaScript puro, Canvas, dark mode, mobile-first)
```

### Responsabilidades

| Camada | Faz | NÃO faz |
|---|---|---|
| **ESP32** | Fala com o ELM327, descobre PIDs suportados, executa a assinatura recebida, **carimba o horário da leitura**, envia bytes crus | Não decodifica, não decide, não conhece gráficos |
| **Backend** | Mantém catálogo de PIDs, decodifica fórmulas, guarda último valor, avalia e monta gráficos, decide o que ler e em que ritmo, distribui aos clientes | Não fala com o carro diretamente |
| **Frontend** | Recebe config e valores prontos, desenha no Canvas | Não calcula nada importante |

---

## 3. Decisões já tomadas (não reabrir sem motivo)

| Tema | Decisão |
|---|---|
| Leitor OBD | **ELM327 Bluetooth Clássico** (não BLE). Exige **ESP32 original** (não S3/C3). |
| CAN nativa (TWAI) | **Fora do MVP.** Existe só no diagrama como extensão (`NativeCANAdapter`). |
| Fórmulas de decodificação | **No backend** (`PidDecoder` + `PIDDescriptor`). ESP32 manda bytes crus. |
| Escopo de PIDs | Mode 01 padrão J1979. PIDs proprietários (Mode 22), CAN 29-bit, K-Line: **fora**. |
| Polling | **Assinatura:** backend envia `subscribe` uma vez, ESP32 lê sozinho e empurra os dados. Não é "um pedido por leitura". |
| Multi-PID | CAN permite até **6 PIDs por pedido** (`01 0C 0D 05`). A interface já é `readPids(lista)`. MVP começa lendo 1 por vez. Depois: multi-PID com **fallback** para 1 PID se o clone ELM falhar. |
| Dica de velocidade ELM | Acrescentar o nº de respostas esperadas ao comando (`010C1`) evita esperar o timeout. |
| Descoberta de PIDs | Consulta em cadeia: `0100`, `0120`, `0140`... enquanto o último bit do bitmask estiver ligado. |
| Timestamp | **Carimbado no ESP32** no momento da leitura. |
| Sincronia X×Y | **Sample-and-hold** no backend (`LatestValueStore`) + idade máxima (`MaxAgeMs`) por eixo. |
| Gráficos | Cada gráfico é uma Strategy com lista de `ChartAxis` (2 ou mais parâmetros). Estados: `Ready`, `Partial`, `Unavailable`. |
| PIDs sem gráfico | **Todos os PIDs soltos são enviados** ao frontend mesmo sem gráfico. |
| Eixo de tempo | `ChartAxis.IsTime = true` (eixo virtual). Tempo não é PID. |
| Singleton | `TelemetryHub` via `AddSingleton` (injeção de dependência do .NET). **Não** escrever `static Instance` manual. |
| Vários clientes | O Hub distribui a vários navegadores (Observer). Hoje 1 carro, mas `deviceId` nas mensagens. |
| Protocolos | ESP32 usa **WebSocket puro** (`/ws/device`). Navegadores usam **SignalR** (`/hubs/telemetry`). O ESP32 não fala SignalR. |
| Exportação CSV/PDF | **Fora do MVP.** Só prevista na arquitetura (`DataBuffer`). |
| Diagrama | **Conceitual**, independente de linguagem. Backend em C#, firmware em C++, frontend em JS. |

---

## 4. Classes (diagrama revisado)

### Firmware (C++)
- `ESP32TelemetryAgent`: orquestra; conecta no backend, envia `hello` e `supportedPids`, recebe `subscribe`, roda o loop de leitura.
- `IOBDAdapter` (interface): `init`, `getProtocolName`, `getSupportedPids`, `readPids(lista)`.
- `ELM327BluetoothAdapter`: implementação real via Bluetooth Classic; monta comandos, parseia texto, fallback para 1 PID.
- `MockAdapter`: dados falsos para testar o ESP32 sem carro.
- `NativeCANAdapter`: **futuro**.
- `SubscriptionPlan` / `PollGroup`: grupos de até 6 PIDs + período.
- `RawReading`: `PidKey`, bytes, `deviceTimestampMs`.
- `PidKey`: modo + pid.

### Backend (C#)
- `TelemetryHub` (singleton): cérebro. `OnDeviceConnected`, `EvaluateAndGetReadyCharts`, `BuildAndSendSubscription`, `ProcessReading`, `OnClientConnected`, `OnDeviceDisconnected`.
- `ESP32Service`: gerencia o WebSocket do dispositivo; dispara eventos; `SendAsync`.
- `SubscriptionPlanner`: monta grupos de até 6 PIDs (no MVP pode ser método do Hub).
- `PidCatalog`: dicionário `PidKey` → `PIDDescriptor`.
- `PIDDescriptor`: **imutável**; chave, nome, unidade, nº de bytes da resposta, min, max, id da fórmula.
- `PidDecoder`: bytes → valor físico.
- `PidReading`: valor decodificado + horário.
- `LatestValueStore`: último valor por PID (`ConcurrentDictionary`); `TryGetFresh(pid, maxAgeMs, nowMs)`.
- `IChartStrategy` / `ChartStrategyBase` (abstrata) / `RpmVsSpeedChartStrategy` / `MapVsTimeChartStrategy`.
- `ChartAxis`: papel, PID, `IsTime`, `Required`, `MaxAgeMs`.
- `ChartPoint`, `ChartConfigDto`, enums `ChartStatus` e `ChartType`.
- `ClientBroadcaster`: envia aos navegadores via `IHubContext` do SignalR.
- `TelemetryClientHub`: classe `Hub` do SignalR, quase vazia (só para os navegadores conectarem).

### Frontend (JS puro)
- `WebDashboard`: SignalR, `buildUI`, `updateTable`, `onPidValue`, `onChartPoint`, estado offline.
- `CanvasChartRenderer`: linha, dispersão, gauge.
- `DataBuffer`: **pós-MVP** (exportação).

### Padrões de projeto
- **Adapter:** `IOBDAdapter` e implementações.
- **Strategy:** `IChartStrategy`.
- **Template Method:** `ChartStrategyBase`.
- **Singleton:** `TelemetryHub` (via DI).
- **Observer:** `ClientBroadcaster` (vários navegadores assinam).

---

## 5. O que vai para o código e o que não vai

**MVP (código):** tudo do firmware exceto `NativeCANAdapter`; tudo do backend; `WebDashboard` e `CanvasChartRenderer`.

**Simplificado no MVP:**
- `PidKey` com modo fixo `01`.
- Um único período de leitura (sem faixas rápido/médio/lento).
- `SubscriptionPlanner` pode ser método dentro do Hub.
- `ChartStatus.Partial` e `ChartType.Gauge/Scatter` entram só se sobrar tempo (começar com `Line`).
- Multi-PID com fallback fica para a semana 5 ou 6.
- Catálogo com 10 a 15 PIDs principais.

**Só no diagrama:** `NativeCANAdapter`, `DataBuffer` de exportação, outros modos de PID.

---

## 6. Fórmulas Mode 01 (catálogo inicial)

| PID | Nome | Bytes | Fórmula | Unidade |
|---|---|---|---|---|
| 0x04 | Carga calculada do motor | 1 | `A * 100 / 255` | % |
| 0x05 | Temperatura do líquido de arrefecimento | 1 | `A - 40` | °C |
| 0x0B | Pressão do coletor (MAP) | 1 | `A` | kPa |
| 0x0C | Rotação do motor | 2 | `(256*A + B) / 4` | rpm |
| 0x0D | Velocidade | 1 | `A` | km/h |
| 0x0F | Temperatura do ar de admissão | 1 | `A - 40` | °C |
| 0x11 | Posição do acelerador (TPS) | 1 | `A * 100 / 255` | % |

**Teste de referência:** bytes `1A F8` no PID `0x0C` devem decodificar para **1726 rpm**.

---

## 7. Fluxo de informação

### Fase 1: conexão e descoberta
1. ESP32 conecta no ELM327 (`init`). O ELM detecta o protocolo.
2. ESP32 descobre os PIDs suportados (`0100`, `0120`...).
3. ESP32 conecta no backend e envia `hello` e `supportedPids`. **Não interpreta nada.**

### Fase 2: backend decide
`TelemetryHub.OnDeviceConnected`: monta o catálogo dos PIDs suportados, roda `Evaluate(supportedPids)` em cada gráfico (`Ready`/`Partial`/`Unavailable`) e escolhe os PIDs a ler (os dos gráficos prontos + todos os soltos).

### Fase 3: navegador monta a tela
Backend envia o catálogo completo e a lista de gráficos (`ChartConfigDto`). O frontend executa `buildUI`.

### Fase 4: assinatura
Hub envia `subscribe` (grupos de até 6 PIDs + período). O ESP32 passa a trabalhar sozinho.

### Fase 5: loop
- **ESP32:** dispara o grupo, carimba o horário, envia `rawReading` (PID, bytes, horário).
- **Backend (`ProcessReading`):** decodifica, guarda no `LatestValueStore`, envia `pidValue` (valor solto) a todos os clientes, pergunta aos gráficos que usam o PID se dá para montar ponto (`TryBuildPoint`), e envia `chartPoint`.
- **Navegador:** atualiza tabela e desenha no Canvas.

### Regras do `TryBuildPoint`
- Todos os eixos obrigatórios precisam ter valor.
- Nenhum valor pode estar mais velho que `MaxAgeMs` do eixo (ex.: RPM/velocidade ~300 ms; temperatura ~5 s).
- Se falhar, **não gera ponto** (retorna vazio) e o gráfico fica "sem dados atualizados".
- O ponto é gerado quando chega leitura nova do **eixo mais rápido**.

### Casos especiais
- ELM falha no multi-PID: volta a 1 PID por vez e avisa o backend.
- PID para de responder: o gráfico estoura `MaxAgeMs` e é marcado como desatualizado.
- ESP32 desconecta: Hub marca tudo offline e avisa os navegadores.
- Novo navegador entra: recebe catálogo e gráficos e segue ao vivo.

---

## 8. Contrato de mensagens (atualizado)

Toda mensagem tem o campo `type`. O Hub faz `switch` por ele.

```json
// ESP32 -> Backend
{ "type": "hello", "deviceId": "esp32-01", "adapter": "ELM327_BT", "protocol": "ISO 15765-4 CAN 11/500" }
{ "type": "supportedPids", "deviceId": "esp32-01", "pids": ["04","05","0B","0C","0D"] }
{ "type": "rawReading", "deviceId": "esp32-01", "pid": "0C", "data": "1AF8", "ts": 123456 }

// Backend -> ESP32
{ "type": "subscribe", "groups": [ { "periodMs": 200, "pids": ["0C","0D","0B"] } ] }
```

Eventos SignalR para os navegadores (nomes provisórios): `catalog`, `charts`, `pidValue`, `chartPoint`, `deviceStatus`.

---

## 9. Ordem de codificação

| Passo | O que fazer | Pronto quando |
|---|---|---|
| 0 | Fechar o contrato de mensagens (seção 8) | Documento com os formatos |
| 1 | Projeto do backend (estrutura do ACRH sem Shared Memory) | Compila e sobe vazio |
| 2 | Modelos puros: `PidKey`, `PIDDescriptor`, `PidReading`, `RawReading`, `PidCatalog` (5 PIDs) | Compila |
| 3 | `PidDecoder` **com testes** (EU escrevo) | Testes passam (1A F8 = 1726 rpm) |
| 4 | `LatestValueStore` **com testes de idade máxima** (EU escrevo) | Testes passam |
| 5 | `ChartAxis`, `IChartStrategy`, `ChartStrategyBase`, `RpmVsSpeed` **com testes** (EU escrevo) | Testes passam |
| 6 | `ESP32Service` + `/ws/device` | Recebe mensagem de teste (wscat/Postman) |
| 7 | `TelemetryHub` + `ClientBroadcaster` + `TelemetryClientHub` | Fluxo completo no backend |
| 8 | **Simulador de dispositivo em C#** (console que fala `/ws/device`, andaime de dev, não entra no diagrama) | Dados falsos chegam ao Hub |
| 9 | Frontend: tabela de PIDs, depois gráfico de linha | Valores na tela |
| 10 | Firmware ESP32 + ELM327 real (semana 5) | ESP32 lendo o carro |
| 11 | Multi-PID com fallback, testes no carro, documentação (semana 6) | Entrega |

**Primeira fatia vertical a buscar:** simulador → backend → navegador mostrando um RPM. Só depois completar camadas.

---

## 10. Pendências e dúvidas em aberto

- Fechar o contrato de mensagens (seção 8 é rascunho).
- Escolher biblioteca de WebSocket e de Bluetooth no ESP32 (Arduino `BluetoothSerial` + biblioteca de WebSocket cliente).
- Decidir se `SubscriptionPlanner` vira classe ou método do Hub.
- Definir a política exata de geração de pontos quando há mais de dois eixos.
- Diagrama de sequência e estrutura de pastas ainda não feitos.

---

## 11. Diagrama de classes completo (Mermaid)

(O diagrama permanece igual, pois já reflete o fluxo discutido.)
