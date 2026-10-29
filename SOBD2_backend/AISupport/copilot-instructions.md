# Contexto do Projeto: Telemetria OBD-II em Tempo Real

> Leia este arquivo antes de sugerir alterações. Responda em português do Brasil, de forma didática e objetiva.
> O usuário precisa entender e conseguir defender as decisões do código na apresentação do projeto.

## 1. Objetivo

Construir uma plataforma de telemetria automotiva em tempo real para veículos compatíveis com OBD-II padrão SAE J1979, inicialmente validada com um Chevrolet Celta.

```text
ECU do carro
  ↕ OBD-II / CAN
ELM327 Bluetooth Clássico
  ↕ Bluetooth SPP
ESP32
  ↕ WebSocket puro: /ws/device
Backend C# / .NET 9
  ↕ SignalR: /hubs/telemetry
Frontend JavaScript
```

O projeto usa .NET 9, C# moderno e `Microsoft.NET.Sdk.Web`.

## 2. Responsabilidades

| Camada | Responsabilidades | Não deve fazer |
|---|---|---|
| ESP32 | Conversar com o ELM327, descobrir PIDs, executar a assinatura, carimbar timestamp e enviar bytes crus | Decodificar fórmulas, decidir gráficos ou conhecer regras de apresentação |
| Backend | Receber mensagens, validar PIDs, decodificar bytes, manter últimos valores, gerar pontos dos gráficos e distribuir dados | Acessar o carro diretamente |
| Frontend | Receber valores e pontos prontos via SignalR e apresentar tabela/gráficos | Fazer cálculos de negócio ou decodificação OBD-II |

O backend é o cérebro. O ESP32 envia leituras automaticamente depois de receber uma assinatura; o backend não deve pedir cada leitura individualmente.

## 3. Decisões de arquitetura

- O ESP32 usa WebSocket puro em `/ws/device`.
- O navegador usa SignalR em `/hubs/telemetry`.
- `ESP32Service` mantém o WebSocket e expõe eventos `OnMessage` e `OnDisconnected`.
- `TelemetryHub` é um singleton criado por injeção de dependência. O `Program.cs` força sua criação para que ele se inscreva no evento do `ESP32Service` durante a inicialização.
- `LatestValueStore` usa `ConcurrentDictionary<PidKey, PidReading>` e guarda o último valor de cada PID.
- `PidCatalog` fornece os metadados do PID.
- `PidDecoder` converte o hexadecimal recebido em valor físico. O `Formula` do descriptor documenta a fórmula, mas atualmente a execução é feita por `switch` no decoder.
- `ClientBroadcaster` usa `IHubContext<TelemetryClientHub>` para publicar eventos SignalR.
- `TelemetryClientHub` é o Hub SignalR dos navegadores e não deve conter a lógica de negócio da telemetria.
- O padrão Strategy é usado para os gráficos: cada strategy declara PIDs necessários e monta um `ChartPoint`.
- O padrão Adapter para separar transportes/dispositivos ainda é uma pendência. Não alterar `ESP32Service` para adapter sem planejar a migração de DI e do endpoint.

## 4. Fluxo completo

1. O backend inicia em `http://0.0.0.0:5000`.
2. O `Program.cs` registra os singletons, habilita WebSocket, CORS e arquivos estáticos, mapeia `/ws/device` e `/hubs/telemetry`.
3. O ESP32 conecta em `/ws/device`.
4. O endpoint aceita o socket e chama `ESP32Service.HandleConnectionAsync`.
5. O ESP32 descobre PIDs suportados no carro e envia imediatamente `supportedPids`.
6. `ESP32Service` recebe o texto e dispara `OnMessage`.
7. `TelemetryHub`, inscrito nesse evento, desserializa a mensagem.
8. Para `supportedPids`, converte cada PID hexadecimal para `PidKey` com modo `0x01`, guarda em `_supportedPids` e envia uma mensagem `subscribe` ao ESP32.
9. O ESP32 executa a assinatura localmente e envia `rawReading` sem novo pedido do backend para cada amostra.
10. Para `rawReading`, o Hub valida o PID, busca o `PIDDescriptor`, decodifica `data`, cria `PidReading` e atualiza o `LatestValueStore`.
11. O Hub chama `ClientBroadcaster.BroadcastLatestAsync`, enviando o snapshot dos PIDs pelo evento SignalR `pidValue`.
12. O Hub chama `EvaluateCharts`, que percorre as strategies configuradas, gera pontos possíveis e os envia pelo evento SignalR `chartPoint`.
13. O frontend atualiza a tabela e o gráfico em tempo real.

Eventos C# são callbacks/listeners: `OnMessage += HandleDeviceMessageAsync` registra um método para ser executado quando o evento é disparado. Não são triggers de banco de dados. O `await OnMessage(text)` executa os handlers inscritos e aguarda seu processamento.

## 5. Contrato de mensagens

As mensagens devem respeitar exatamente os nomes definidos no contrato. As mensagens enviadas pelo ESP32 usam `Type`, `DeviceId`, `Pid`, `Data`, `Timestamp` e `Pids`. A mensagem `subscribe` enviada pelo backend usa `type`, `groups`, `periodMs` e `pids`.

O contrato oficial atual utiliza `Timestamp`. Não usar `Ts` sem implementar compatibilidade explícita no `DeviceMessage` com `JsonPropertyName` ou outro mapeamento equivalente.

### ESP32 → backend

```json
{"Type":"hello","DeviceId":"esp32-mock","Adapter":"ELM327_BT","Protocol":"ISO 15765-4 CAN 11/500"}
{"Type":"supportedPids","DeviceId":"esp32-mock","Pids":["04","05","0B","0C","0D"]}
{"Type":"rawReading","DeviceId":"esp32-mock","Pid":"0C","Data":"1AF8","Timestamp":123456}
```

### Backend → ESP32

```json
{"type":"subscribe","groups":[{"periodMs":200,"pids":["04","05","0B","0C","0D"]}]}
```

A leitura `Data: "1AF8"` para o PID `0C` representa `(0x1A * 256 + 0xF8) / 4 = 1726 rpm`.

### Eventos SignalR → frontend

- `deviceStatus`: conexão do cliente SignalR e estado do dispositivo quando implementado.
- `pidValue`: snapshot dos últimos valores.
- `chartPoint`: ponto produzido por uma strategy.
- `catalog` e `charts`: previstos para configuração futura do frontend.

Formato atual aproximado de `pidValue`:

```json
[{"pid":"01-0C","value":1726,"timestamp":123456}]
```

Formato atual de `chartPoint`:

```json
{"chart":"RPM vs Tempo","x":123456,"y":[1726],"timestamp":123456}
```

## 6. Arquivos atuais e responsabilidades

### Entrada e infraestrutura

- `Program.cs`: DI, CORS permissivo para desenvolvimento, arquivos estáticos, endpoint WebSocket, endpoint SignalR e criação do `TelemetryHub`.
- `Hubs/TelemetryClientHub.cs`: herda de `Hub` e envia `deviceStatus` ao cliente conectado.
- `Services/ESP32Service.cs`: recebe fragmentos/mensagens WebSocket, remonta mensagens completas, dispara eventos e envia JSON ao dispositivo.
- `Services/ClientBroadcaster.cs`: publica `pidValue` e `chartPoint` via `IHubContext<TelemetryClientHub>`.

### Domínio OBD-II

- `Entities/PidKey.cs`: struct imutável com `Mode`, `Pid`, igualdade, hash e representação hexadecimal.
- `Entities/PIDDescriptor.cs`: metadados imutáveis do PID: chave, nome, unidade, quantidade de bytes, limites e fórmula documentada.
- `Services/PidCatalog.cs`: catálogo atual de PIDs Mode 01.
- `Services/PidDecoder.cs`: recebe `PIDDescriptor` e string hexadecimal, converte bytes e aplica a fórmula conhecida pelo PID.
- `Services/LatestValueStore.cs`: guarda `PidReading` por `PidKey` em `ConcurrentDictionary`.

### Gráficos

- `Services/IChartStrategy.cs`: contrato de uma strategy e classe `ChartPoint`.
- `RpmVsTimeChartStrategy.cs`: strategy atual de RPM no tempo, exigindo PID `01-0C`.
- `Services/TelemetryHub.cs`: processa mensagens, atualiza store, executa strategies e chama o broadcaster.
- `frontend_teste/index.html`: cliente SignalR de teste; exibe PIDs, logs e desenha uma linha simples em Canvas.

## 7. PIDs atualmente catalogados

Mode 01:

| PID | Nome | Bytes | Fórmula | Unidade |
|---|---|---:|---|---|
| 04 | Carga calculada | 1 | `A * 100 / 255` | % |
| 05 | Temperatura do líquido | 1 | `A - 40` | °C |
| 0B | Pressão MAP | 1 | `A` | kPa |
| 0C | Rotação | 2 | `(256*A + B) / 4` | rpm |
| 0D | Velocidade | 1 | `A` | km/h |
| 0F | Temperatura do ar | 1 | `A - 40` | °C |
| 10 | Vazão MAF | 2 | `(256*A + B) / 100` | g/s |
| 11 | Posição do acelerador | 1 | `A * 100 / 255` | % |
| 0E | Avanço da ignição | 1 | `(A / 2) - 64` | ° |
| 1F | Temperatura do combustível | 1 | `A - 40` | °C |
| 2F | Nível de combustível | 1 | `A * 100 / 255` | % |
| 46 | Temperatura ambiente | 1 | `A - 40` | °C |

O catálogo é independente da disponibilidade real do carro. A lista recebida em `supportedPids` define os PIDs que podem ser aceitos para a sessão atual.

## 8. Regras atuais dos gráficos

- Não há enumeração de tipos de gráfico no contrato atual. O conceito é um gráfico de X contra um ou mais Y.
- Um ponto tem um X, um vetor de Y e timestamp.
- O caso comum é X = tempo de observação e Y = um ou mais PIDs.
- No futuro, X e qualquer Y poderão ser funções que combinem dois ou mais `PidReading`; a strategy deve declarar todos os PIDs necessários e realizar a combinação no backend.
- Um gráfico pode ter vários Y, por isso `ChartPoint.Y` é `double[]`.
- O frontend apenas desenha o ponto recebido; não deve recomputar a regra de negócio.
- Não existe status `Partial`. Um gráfico só é considerado disponível quando todos os `RequiredPids` estão em `_supportedPids`; caso contrário, a strategy não é avaliada.
- PIDs disponíveis que não participam de gráfico continuam sendo enviados no evento `pidValue` e podem ser mostrados como informações isoladas.
- `EvaluateCharts` percorre todas as strategies configuradas. No futuro, deve-se separar strategies disponíveis/ativas quando o número crescer, filtrando-as após `supportedPids` e avaliando somente as ativas.
- O `RpmVsTimeChartStrategy` exige `01-0C`, usa timestamp como X, aceita leitura com até 500 ms e produz Y com RPM.
- Se faltar dado no snapshot ou ele estiver expirado, a strategy retorna `null` e nenhum `chartPoint` é enviado.
- A regra de sample-and-hold e `MaxAgeMs` por eixo ainda precisa ser formalizada nas classes de gráfico; não inventar essa API sem alinhar primeiro.

## 9. Frontend de teste

O arquivo `frontend_teste/index.html` usa SignalR 8.0.7 por CDN e registra handlers para `deviceStatus`, `pidValue` e `chartPoint`. Quando servido pelo próprio backend, deve usar URL relativa `/hubs/telemetry` e ser acessado por HTTP, nunca por `file://`.

Para Live Server (normalmente `127.0.0.1:5500`), a URL deve ser absoluta:

```javascript
.withUrl("http://localhost:5000/hubs/telemetry")
```

Nesse caso, CORS precisa estar habilitado no backend. A política atual é permissiva para desenvolvimento (`SetIsOriginAllowed(_ => true)`, métodos/headers e credenciais). Em produção, substituir por origens explícitas.

`UseDefaultFiles` e `UseStaticFiles` estão no pipeline, mas o frontend só será servido pelo backend se estiver em `wwwroot`; a pasta atual `frontend_teste` não é automaticamente `wwwroot`.

## 10. Simulador e validação já realizados

O mock do ESP32 conecta em `ws://localhost:5000/ws/device`, envia `supportedPids`, recebe `subscribe` e envia várias leituras `rawReading` em sequência.

O teste validou:

- desserialização da lista de PIDs;
- conversão de strings hexadecimais para `PidKey`;
- envio de `subscribe` pelo WebSocket;
- filtragem de PID suportado;
- decodificação `1AF8` → `1726 rpm`;
- atualização do `LatestValueStore`;
- geração do ponto `RPM vs Tempo`;
- publicação de `pidValue` e `chartPoint` para clientes SignalR.

Ao depurar, mensagens de carregamento de DLL não indicam falha. Os logs importantes são os emitidos por `TelemetryHub`.

## 11. Pendências para concluir o backend

1. Criar testes automatizados para `PidDecoder`, `LatestValueStore`, `TelemetryHub` e strategies.
2. Manter o campo oficial `Timestamp` no contrato. Só adicionar compatibilidade com `Ts` se houver necessidade real e mapeamento explícito no `DeviceMessage`.
3. Separar DTOs e `DeviceMessage` de `TelemetryHub` em arquivos próprios quando estabilizar o contrato.
4. Implementar catálogo/configuração inicial para o frontend via SignalR.
5. Implementar status de conexão/desconexão do dispositivo e propagá-lo aos navegadores.
6. Implementar strategies adicionais e, se necessário, uma coleção de strategies ativas filtrada por PIDs suportados.
7. Formalizar e implementar combinações de PIDs em eixos X/Y e regras de idade por eixo.
8. Criar o `IDeviceAdapter` e migrar `ESP32Service` para uma arquitetura Adapter sem quebrar o endpoint nem a DI.
9. Adicionar validação de tamanho/paridade de hexadecimal e quantidade esperada de bytes no decoder.
10. Adicionar logs estruturados e remover `Console.WriteLine` de diagnóstico quando o MVP estiver estabilizado.
11. Definir política de múltiplas conexões/dispositivos; o estado atual mantém um único socket e um único conjunto de PIDs.

## 12. Ordem recomendada a partir daqui

1. Validar o frontend SignalR com o simulador.
2. Adicionar testes unitários para decoder/store/strategies.
3. Estabilizar DTOs e contrato JSON.
4. Implementar mais strategies e configuração de charts.
5. Implementar `IDeviceAdapter`/adapters do ESP32 e MockAdapter.
6. Integrar ELM327 Bluetooth real.
7. Testar com o carro e tratar fallback multi-PID.

Não alterar arquivos fora do escopo solicitado. Antes de escrever código, explicar o fluxo e confirmar a regra de negócio quando houver ambiguidade.

## 13. Estado real revisado da aplicação

O fluxo principal do MVP está implementado:

- `Program.cs` configura DI, CORS, arquivos estáticos, WebSocket e SignalR.
- `ESP32Service` recebe mensagens WebSocket, remonta fragmentos, dispara `OnMessage` e envia JSON ao dispositivo.
- `TelemetryHub` processa `supportedPids` e `rawReading`.
- `PidDecoder` já existe e converte dados hexadecimais em valores físicos.
- `PidCatalog` fornece os descritores dos PIDs Mode 01.
- `LatestValueStore` guarda o último `PidReading` por `PidKey`.
- `ClientBroadcaster` publica `pidValue` e `chartPoint` via SignalR.
- `TelemetryClientHub` é o Hub SignalR dos navegadores e não contém regras de negócio.
- `RpmVsTimeChartStrategy` é a única Strategy de gráfico implementada atualmente.
- `frontend_teste/index.html` recebe os eventos SignalR, atualiza a tabela e desenha o gráfico.

`DeviceMessage` está atualmente declarado no final de `TelemetryHub.cs`. A separação para um arquivo próprio é uma melhoria futura, não assumir que esse arquivo já existe.

`ESP32Service` expõe `OnDisconnected`, mas o `TelemetryHub` ainda não está inscrito nesse evento para publicar o estado de desconexão aos navegadores.

As classes `RpmVsSpeedChartStrategy`, `MapVsTimeChartStrategy`, `IDeviceAdapter`, `ELM327Adapter`, `NativeCANAdapter` e `MockAdapter` são planejadas ou pertencem ao firmware futuro. Não apresentá-las como implementadas no backend atual.

## 14. Regras para o firmware e o simulador

O ESP32 deve permanecer como transportador de dados:

- não aplicar fórmulas OBD-II;
- não converter bytes para RPM, velocidade ou temperatura;
- não decidir quais gráficos serão exibidos;
- enviar os bytes crus recebidos do ELM327;
- enviar o timestamp da leitura;
- executar a assinatura recebida do backend sem solicitar cada leitura individualmente.

O simulador pode gerar bytes hexadecimais artificialmente, mas isso serve apenas para produzir dados de teste. A lógica de decodificação continua exclusivamente no backend.

Para o PID `0C`, o valor `Data` é formado pelos bytes crus `A` e `B`. O backend aplica `(256 * A + B) / 4`. O ESP32 real não deve fazer essa multiplicação ou divisão.

## 15. Diagramas e documentação

`AISupport/UseCaseDiagram.txt` contém atualmente um diagrama de classes, apesar do nome do arquivo.

Ao atualizar o diagrama:

- representar primeiro as classes realmente existentes;
- mostrar `ESP32Service`, `TelemetryHub`, `PidCatalog`, `PidDecoder`, `LatestValueStore`, `ClientBroadcaster` e `TelemetryClientHub`;
- representar ESP32 e ELM327 como sistemas externos quando não houver código deles nesta solution;
- mostrar WebSocket entre ESP32 e backend;
- mostrar SignalR entre backend e frontend;
- marcar classes futuras como `<<planejado>>`;
- não incluir classes planejadas como se já estivessem implementadas;
- usar sintaxe Mermaid compatível e simples, evitando assinaturas genéricas complexas quando o renderizador não as suportar.

## 16. Critérios para futuras alterações

Antes de alterar o código:

1. Ler este arquivo e os arquivos diretamente relacionados.
2. Distinguir implementação atual de planejamento.
3. Preservar os contratos JSON existentes.
4. Manter o ESP32 sem regras de negócio.
5. Explicar decisões importantes em português do Brasil.
6. Preferir alterações pequenas e testáveis.
7. Validar a compilação após alterações no backend.
8. Executar testes relacionados quando existirem.
9. Informar arquivos criados, editados e pendências.
10. Não adicionar Design Patterns sem necessidade real.

O próximo foco recomendado é estabilizar o fluxo existente, criar testes para `PidDecoder`, `LatestValueStore` e `RpmVsTimeChartStrategy`, e tratar a desconexão do dispositivo antes de expandir a arquitetura.
