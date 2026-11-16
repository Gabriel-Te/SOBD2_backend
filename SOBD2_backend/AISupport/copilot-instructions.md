# Contexto do Projeto: Telemetria OBD-II em Tempo Real
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

O projeto utiliza .NET 9, C# moderno e `Microsoft.NET.Sdk.Web`.

## 2. Responsabilidades das camadas

| Camada | Responsabilidades | Não deve fazer |
|---|---|---|
| ESP32 | Conversar com o ELM327, descobrir PIDs, executar a assinatura, carimbar timestamp e enviar bytes crus | Decodificar fórmulas, decidir gráficos ou conhecer regras de apresentação |
| Backend | Receber mensagens, validar PIDs, decodificar bytes, manter últimos valores, gerar pontos dos gráficos e distribuir dados | Acessar o carro diretamente |
| Frontend | Receber valores e pontos prontos via SignalR e apresentar a telemetria | Fazer cálculos de negócio ou decodificação OBD-II |

O backend é o cérebro da aplicação. Depois de receber uma assinatura, o ESP32 consulta e envia as leituras automaticamente. O backend não solicita cada leitura individualmente.

## 3. Arquitetura implementada

- O ESP32 usa WebSocket puro em `/ws/device`.
- O navegador usa SignalR em `/hubs/telemetry`.
- `ESP32Service` mantém o WebSocket e expõe `OnMessage` e `OnDisconnected`.
- `TelemetryHub` é registrado como singleton. O `Program.cs` força sua criação para que ele se inscreva no evento `OnMessage` durante a inicialização.
- `LatestValueStore` usa `ConcurrentDictionary<PidKey, PidReading>` e guarda o último valor de cada PID em memória.
- `PidCatalog` fornece nome, unidade, quantidade de bytes, limites e fórmula documentada dos PIDs conhecidos.
- `PidDecoder` converte o hexadecimal recebido em valor físico. A propriedade `Formula` documenta a regra; a execução atual é feita por `switch`.
- `ClientBroadcaster` usa `IHubContext<TelemetryClientHub>` para publicar `pidValue` e `chartPoint`.
- `TelemetryClientHub` é o Hub SignalR dos navegadores e não contém lógica de negócio da telemetria.
- O padrão Strategy é usado para os gráficos. Cada strategy declara os PIDs necessários e monta um `ChartPoint`.
- O padrão Adapter ainda não foi implementado. Não alterar `ESP32Service` para Adapter sem planejar a migração de DI e do endpoint.
- MongoDB ainda não foi implementado. A persistência histórica é uma etapa futura.

## 4. Fluxo completo implementado

1. O backend inicia em `http://0.0.0.0:5000`.
2. O `Program.cs` registra os serviços, habilita WebSocket, CORS e arquivos estáticos, e mapeia `/ws/device` e `/hubs/telemetry`.
3. O ESP32 conecta em `/ws/device`.
4. O endpoint aceita o socket e chama `ESP32Service.HandleConnectionAsync`.
5. O ESP32 descobre os PIDs suportados e envia `supportedPids`.
6. `ESP32Service` remonta mensagens fragmentadas e dispara `OnMessage`.
7. `TelemetryHub` desserializa a mensagem.
8. Em `supportedPids`, o Hub converte os PIDs para `PidKey` no modo `0x01`, guarda `_supportedPids` e envia `subscribe`.
9. O ESP32 executa a assinatura localmente e envia `rawReading`.
10. Em `rawReading`, o Hub valida o PID, busca o descriptor, decodifica `Data`, cria `PidReading` e atualiza o `LatestValueStore`.
11. O Hub publica o snapshot pelo evento SignalR `pidValue`.
12. O Hub avalia todas as strategies disponíveis e publica pontos pelo evento `chartPoint`.
13. O frontend atualiza os valores e os gráficos.

Eventos C# são callbacks/listeners. A instrução `OnMessage += HandleDeviceMessageAsync` registra um handler; `await OnMessage(text)` executa e aguarda os handlers inscritos.

## 5. Contratos de mensagens

Os nomes do contrato devem ser preservados.

### ESP32 → backend

```json
{"Type":"hello","DeviceId":"esp32-carro-01","Adapter":"ELM327_BT","Protocol":"OBD-II"}
{"Type":"supportedPids","DeviceId":"esp32-carro-01","Pids":["04","05","0B","0C","0D"]}
{"Type":"rawReading","DeviceId":"esp32-carro-01","Pid":"0C","Data":"1AF8","Timestamp":123456}
```

O campo oficial é `Timestamp`. Não substituir por `Ts` sem compatibilidade explícita no DTO.

### Backend → ESP32

```json
{"type":"subscribe","groups":[{"periodMs":200,"pids":["04","05","0B","0C","0D"]}]}
```

O ESP32 interpreta `periodMs` como intervalo entre ciclos de consulta. Como os PIDs são consultados sequencialmente, o intervalo efetivo de atualização de cada PID pode ser maior.

### SignalR → frontend

Evento `pidValue` atual do código:

```json
[{"pid":"01-0C","value":1726,"timestamp":123456}]
```

O contrato planejado para a interface com nomes e unidades é:

```json
[{"pid":"01-0C","name":"Rotação do motor","unit":"rpm","value":1726,"timestamp":123456}]
```

Esse formato só deve ser documentado como implementado depois que `ClientBroadcaster` for alterado para enviar `name` e `unit`.

Evento `chartPoint`:

```json
{"chart":"RPM vs Tempo","x":123456,"y":[1726],"timestamp":123456}
```

A estrutura é estável. O significado de `x` e `y` depende da strategy.

## 6. Arquivos e responsabilidades

### Entrada e infraestrutura

- `Program.cs`: DI, CORS, WebSocket, SignalR, arquivos estáticos e endpoints.
- `Hubs/TelemetryClientHub.cs`: Hub dos navegadores; envia o status da conexão SignalR.
- `Services/ESP32Service.cs`: recebe fragmentos WebSocket, remonta mensagens, dispara eventos e envia JSON ao ESP32.
- `Services/TelemetryHub.cs`: processa mensagens do dispositivo, atualiza o store, executa strategies e chama o broadcaster.
- `Services/ClientBroadcaster.cs`: publica eventos SignalR.

### Domínio OBD-II

- `Entities/PidKey.cs`: struct imutável com modo, PID, igualdade, hash e representação hexadecimal.
- `Entities/PIDDescriptor.cs`: metadados do PID.
- `Services/PidCatalog.cs`: catálogo dos PIDs Mode 01 conhecidos.
- `Services/PidDecoder.cs`: conversão de bytes crus para valores físicos.
- `Services/LatestValueStore.cs`: último `PidReading` de cada PID em memória.

`DeviceMessage` ainda está declarado no final de `Services/TelemetryHub.cs`. Não assumir que existe um arquivo DTO separado.

### Frontend atual

`frontend_teste/index.html` é um cliente SignalR de teste. Na versão atual registrada neste documento, ele usa Canvas manual, exibe logs, mostra os últimos valores em tabela e desenha um gráfico único.

A migração para Chart.js, múltiplos gráficos e uma lateral com nome e unidade dos PIDs é planejada. Depois de implementada, esta seção deve ser atualizada.

## 7. PIDs catalogados

Mode 01:

| PID | Nome | Bytes | Fórmula | Unidade |
|---|---|---:|---|---|
| 04 | Carga calculada do motor | 1 | `A * 100 / 255` | % |
| 05 | Temperatura do líquido de arrefecimento | 1 | `A - 40` | °C |
| 0B | Pressão do coletor (MAP) | 1 | `A` | kPa |
| 0C | Rotação do motor | 2 | `(256*A + B) / 4` | rpm |
| 0D | Velocidade | 1 | `A` | km/h |
| 0E | Avanço da ignição | 1 | `(A / 2) - 64` | ° |
| 0F | Temperatura do ar de admissão | 1 | `A - 40` | °C |
| 10 | Vazão de ar (MAF) | 2 | `(256*A + B) / 100` | g/s |
| 11 | Posição do acelerador (TPS) | 1 | `A * 100 / 255` | % |
| 1F | Temperatura do combustível | 1 | `A - 40` | °C |
| 2F | Nível de combustível | 1 | `A * 100 / 255` | % |
| 46 | Temperatura ambiente | 1 | `A - 40` | °C |

O catálogo representa os PIDs conhecidos pelo backend. A lista recebida em `supportedPids` define quais estão disponíveis na sessão atual.

## 8. Strategies implementadas

O `TelemetryHub` configura atualmente estas strategies:

- `RpmVsTimeChartStrategy`: X = timestamp da leitura; Y = RPM (`01-0C`).
- `RpmVsSpeedChartStrategy`: X = velocidade (`01-0D`); Y = RPM (`01-0C`).
- `MapVsRpmChartStrategy`: X = RPM (`01-0C`); Y = MAP (`01-0B`).
- `ThrottleVsRpmChartStrategy`: X = RPM (`01-0C`); Y = TPS (`01-11`).

As strategies de dois PIDs usam `sample-and-hold`: os valores não precisam chegar exatamente juntos. Quando chega uma leitura, a strategy usa o último valor conhecido dos outros PIDs necessários.

Um ponto só é criado quando:

1. todos os PIDs necessários existem no snapshot;
2. todos os valores estão recentes;
3. a idade de cada leitura está entre zero e `MaxAgeMs`.

O limite atual para as strategies é `500 ms`. Se faltar um PID ou algum valor estiver expirado, a strategy retorna `null` e nenhum `chartPoint` é enviado.

O frontend deve manter os pontos separados pelo campo `chart`. Nunca misturar pontos de strategies diferentes.

## 9. Firmware ESP32 validado

O firmware atual:

- cria o Access Point `Telemetry_AP`;
- conecta ao ELM327 por Bluetooth Classic usando o MAC configurado;
- inicializa o ELM327;
- consulta máscaras de PIDs suportados;
- mantém apenas PIDs conhecidos pelo backend;
- conecta ao backend em `192.168.4.2:5000/ws/device`;
- envia `hello` e `supportedPids`;
- recebe `subscribe`;
- ordena os PIDs por prioridade;
- consulta os PIDs sequencialmente;
- envia bytes crus em `rawReading`;
- usa `millis()` no campo `Timestamp`;
- tenta reconectar ao ELM327 e ao backend;
- trata `NO DATA`, `ERROR` e respostas inválidas sem aplicar fórmulas OBD-II.

Para o PID `0C`, o ESP32 envia os bytes `A` e `B`; o backend aplica `(256 * A + B) / 4`. O ESP32 não deve calcular RPM, velocidade, temperatura ou qualquer outro valor físico.

O `hello` é recebido pelo backend, mas atualmente não possui uma rotina de negócio específica; a mensagem é registrada como tipo não tratado. Isso não impede o fluxo de assinatura.

## 10. Estado real do MVP

O fluxo principal foi validado com ESP32, ELM327, backend e frontend:

- inicialização do ELM327;
- descoberta de PIDs;
- conexão WebSocket;
- desserialização de `supportedPids`;
- envio de `subscribe`;
- envio de `rawReading`;
- filtragem de PID suportado;
- decodificação de `1AF8` para `1726 rpm`;
- atualização do `LatestValueStore`;
- geração dos pontos das strategies;
- publicação SignalR de `pidValue` e `chartPoint`.

O MVP atual suporta um único dispositivo e um único socket. Os valores ficam somente em memória.

## 11. Pendências e melhorias

### Prioridade alta

1. Criar testes automatizados para `PidDecoder`, `LatestValueStore`, `TelemetryHub` e strategies.
2. Validar paridade do hexadecimal e quantidade esperada de bytes no decoder.
3. Atualizar `ClientBroadcaster` para enviar `name` e `unit` no evento `pidValue`.
4. Substituir o frontend Canvas por Chart.js com um gráfico separado por strategy.
5. Adicionar a lateral de PIDs com nome, valor, unidade e timestamp decorrido.
6. Corrigir o status de desconexão do dispositivo e propagá-lo aos navegadores.

### Prioridade média

7. Separar `DeviceMessage` e outros DTOs em arquivos próprios.
8. Adicionar catálogo e configuração de gráficos via SignalR.
9. Adicionar logs estruturados e remover diagnósticos diretos por `Console.WriteLine`.
10. Definir política para múltiplas conexões e múltiplos dispositivos.
11. Refinar timeout, retry e tratamento de `NO DATA` do ELM327.
12. Avaliar uma máquina de estados no firmware para reduzir bloqueios durante consultas sequenciais.

### Planejado

13. Criar `IDeviceAdapter` sem quebrar o endpoint nem a DI.
14. Integrar outros adaptadores, como ELM327 real ou CAN nativo, quando aplicável.
15. Criar `ITelemetryRepository`.
16. Implementar persistência histórica em MongoDB.
17. Adicionar consultas históricas, retenção, índices e política de retry.

MongoDB não deve ser adicionado sem solicitação específica. Ele não substituirá o `LatestValueStore`: o store atende o tempo real; o MongoDB atenderá o histórico.

## 12. Regras para futuras alterações

Antes de alterar o código:

1. Ler este documento e os arquivos diretamente relacionados.
2. Distinguir implementação atual de planejamento.
3. Preservar os contratos JSON existentes.
4. Manter o ESP32 sem regras de negócio.
5. Manter a decodificação OBD-II exclusivamente no backend.
6. Não misturar dados de strategies diferentes no frontend.
7. Preferir alterações pequenas, testáveis e compatíveis com .NET 9.
8. Validar a compilação após alterações no backend.
9. Executar testes relacionados quando existirem.
10. Informar arquivos criados, editados e pendências.
11. Não adicionar Design Patterns sem necessidade real.
12. Atualizar este documento quando uma pendência for concluída, removendo a informação antiga em vez de apenas anexar outra versão.

## 13. Execução e validação

O backend deve ser iniciado em `http://0.0.0.0:5000`.

O frontend pode ser servido por Live Server em `http://127.0.0.1:5500`, usando:

```javascript
.withUrl("http://localhost:5000/hubs/telemetry")
```

Quando servido pelo próprio backend, o frontend deve estar em `wwwroot` e usar a URL relativa `/hubs/telemetry`. Não abrir o frontend por `file://`.

A política CORS atual é permissiva para desenvolvimento. Em produção, devem ser definidas origens explícitas.

A ausência da pasta `wwwroot` gera um aviso de arquivos estáticos, mas não impede a comunicação WebSocket ou SignalR quando o frontend é servido externamente.

## 14. Documentação e diagramas

`AISupport/UseCaseDiagram.txt` possui um diagrama de classes apesar do nome do arquivo.

Ao atualizar diagramas:

- representar classes realmente existentes;
- incluir `ESP32Service`, `TelemetryHub`, `PidCatalog`, `PidDecoder`, `LatestValueStore`, `ClientBroadcaster` e `TelemetryClientHub`;
- representar ESP32 e ELM327 como sistemas externos quando não houver código deles nesta solution;
- mostrar WebSocket entre ESP32 e backend;
- mostrar SignalR entre backend e frontend;
- marcar MongoDB, adapters e demais componentes futuros como `<<planejado>>`;
- não representar funcionalidades planejadas como implementadas;
- usar sintaxe simples e compatível com o renderizador utilizado.

O próximo foco recomendado é aplicar e validar o frontend Chart.js, atualizar o contrato `pidValue`, criar os testes automatizados e tratar a desconexão do dispositivo antes de iniciar MongoDB ou Adapter.