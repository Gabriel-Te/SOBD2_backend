Aqui está a versão revisada do README.md, incorporando o novo conteúdo de forma coesa e mantendo a estrutura original:

# Telemetria OBD-II em Tempo Real

> Leia este arquivo antes de sugerir alterações. Responda em português do Brasil, de forma didática e objetiva.
> O usuário precisa entender e conseguir defender as decisões do código na apresentação do projeto.

## Contexto do Projeto

Este projeto visa implementar um sistema de telemetria em tempo real utilizando OBD-II, permitindo a coleta e visualização de dados do veículo de forma eficiente e interativa.

## Estado revisado em 2026-10-27

O backend já foi validado com um mock WebSocket e um frontend SignalR. O fluxo básico funciona: `supportedPids` ? `subscribe` ? `rawReading` ? decodificação ? `LatestValueStore` ? `pidValue`/`chartPoint`.

## Objetivo e arquitetura

ECU / CAN ? ELM327 Bluetooth Clássico ? ESP32 ? WebSocket `/ws/device`
                                                   ?
                                      Backend C# / .NET 9
                                                   ? SignalR `/hubs/telemetry`
                                      Frontend JavaScript / Canvas

- **ESP32**: conversa com o ELM327, descobre PIDs, aplica a assinatura, carimba o timestamp e envia bytes crus.
- **Backend**: valida, decodifica, armazena o último valor, avalia estratégias e distribui dados.
- **Frontend**: exibe valores e pontos recebidos; não executa regras OBD-II.
- **MongoDB**: planejado para histórico; ainda não implementado. `LatestValueStore` continuará sendo apenas estado atual em memória.

## Decisões de arquitetura

- O ESP32 usa WebSocket puro; o navegador usa SignalR.
- O ESP32 não deve receber uma requisição para cada leitura. Após `subscribe`, ele envia leituras automaticamente.
- `ESP32Service` mantém um único socket, remonta mensagens fragmentadas, expõe `OnMessage` e `OnDisconnected` e envia JSON ao dispositivo.
- `TelemetryHub` é singleton via DI e é instanciado no início com `GetRequiredService<TelemetryHub>()` para registrar o handler do evento `ESP32Service.OnMessage`.
- `LatestValueStore` usa `ConcurrentDictionary<PidKey, PidReading>`.
- `PidCatalog` mantém os descritores Mode 01.
- `PidDecoder` converte hexadecimal e aplica as fórmulas conhecidas por PID. A propriedade `Formula` documenta a fórmula, mas ainda não é interpretada dinamicamente.
- `ClientBroadcaster` usa `IHubContext<TelemetryClientHub>` para publicar `pidValue` e `chartPoint`.
- `TelemetryClientHub` é somente o canal SignalR dos navegadores; não contém regra de negócio.
- O Adapter do transporte/dispositivo ainda não foi implementado. A migração de `ESP32Service` para `IDeviceAdapter` deve ser feita depois, sem quebrar a DI ou o endpoint.

## Fluxo operacional

1. `Program.cs` inicia em `http://0.0.0.0:5000`, registra serviços e mapeia WebSocket/SignalR.
2. O ESP32 conecta em `/ws/device`.
3. O ESP32 envia `supportedPids` depois da descoberta no carro.
4. `ESP32Service` dispara `OnMessage`; `TelemetryHub` desserializa.
5. O Hub converte strings hexadecimais em `PidKey` com modo `0x01` e guarda `_supportedPids`.
6. O Hub envia `subscribe` uma vez.
7. O ESP32 envia `rawReading` autonomamente.
8. O Hub valida o PID, busca o `PIDDescriptor`, decodifica `Data`, cria `PidReading` e atualiza o store.
9. O Hub chama `BroadcastLatestAsync` e publica `pidValue`.
10. O Hub chama `EvaluateCharts`; cada strategy disponível pode produzir um `ChartPoint`, publicado como `chartPoint`.

Eventos C# são callbacks/listeners: `OnMessage += HandleDeviceMessageAsync` registra um método para ser chamado quando uma mensagem chega. Não são triggers de banco de dados.

## Contrato JSON

O contrato oficial usa `Timestamp`, não `Ts`. Se houver necessidade de aceitar `Ts`, implementar compatibilidade explicitamente em `DeviceMessage`; não depender de comportamento implícito.

{"Type":"hello","DeviceId":"esp32-mock","Adapter":"ELM327_BT","Protocol":"ISO 15765-4 CAN 11/500"}
{"Type":"supportedPids","DeviceId":"esp32-mock","Pids":["04","05","0B","0C","0D"]}
{"Type":"rawReading","DeviceId":"esp32-mock","Pid":"0C","Data":"1AF8","Timestamp":123456}

{"type":"subscribe","groups":[{"periodMs":200,"pids":["04","05","0B","0C","0D"]}]}


`1AF8` no PID `0C` resulta em `1726 rpm`.

Eventos SignalR atuais:

- `deviceStatus`: estado da conexão SignalR.
- `pidValue`: snapshot dos últimos valores.
- `chartPoint`: ponto de gráfico.
- `catalog` e `charts`: previstos, ainda não publicados.

## Arquivos revisados

- `Program.cs`: DI, CORS permissivo de desenvolvimento, arquivos estáticos, `/ws/device`, `/hubs/telemetry` e criação do `TelemetryHub`.
- `Services/ESP32Service.cs`: transporte WebSocket do dispositivo.
- `Services/TelemetryHub.cs`: processamento, assinatura, decodificação, store, strategies e broadcaster.
- `Services/ClientBroadcaster.cs`: eventos SignalR.
- `Hubs/TelemetryClientHub.cs`: Hub dos clientes.
- `Services/LatestValueStore.cs`: store e `PidReading`.
- `Entities/PidKey.cs`: chave modo/PID; confirmar que a declaração contém `public readonly struct PidKey`.
- `Entities/PIDDescriptor.cs`: descritor imutável.
- `Services/PidCatalog.cs`: catálogo Mode 01.
- `Services/PidDecoder.cs`: decodificador.
- `Services/IChartStrategy.cs`: contrato e `ChartPoint`.
- `RpmVsTimeChartStrategy.cs`: RPM × tempo.
- `RpmVsSpeedChartStrategy.cs`: RPM × velocidade.
- `MapVsRpmChartStrategy.cs`: MAP × RPM.
- `ThrottleVsRpmChartStrategy.cs`: TPS × RPM.
- `frontend_teste/index.html`: cliente de teste SignalR e Canvas.
- `Properties/launchSettings.json`: perfis Visual Studio usam `5134`, mas `Program.cs` chama `app.Run("http://0.0.0.0:5000")`; durante os testes, a porta efetiva é 5000. Não alterar sem alinhar o frontend e o mock.

## PIDs catalogados

Mode 01: `04` carga calculada, `05` temperatura do líquido, `0B` MAP, `0C` RPM, `0D` velocidade, `0E` avanço, `0F` temperatura do ar, `10` MAF, `11` TPS, `1F` temperatura do combustível, `2F` nível de combustível e `46` temperatura ambiente.

O catálogo descreve o que o backend conhece; `supportedPids` descreve o que o carro atual suporta. Só PIDs presentes nos dois conjuntos devem ser assinados/processados.

## Regras dos gráficos

- O modelo não usa tipos de gráfico no backend. Todo gráfico é X contra um ou mais Y.
- `ChartPoint` contém `X`, `double[] Y` e `TimestampMs`.
- X normalmente é o timestamp, mas pode ser um PID; Y pode conter um ou vários valores.
- Uma strategy pode combinar múltiplos PIDs em um eixo. Ela deve declarar todos em `RequiredPids` e fazer a combinação no backend.
- Não existe estado `Partial`: uma strategy só é disponível quando todos os PIDs obrigatórios estão em `_supportedPids`.
- Valores isolados continuam sendo enviados por `pidValue`, mesmo que não participem de gráfico.
- Atualmente `EvaluateCharts` percorre todas as quatro strategies da lista e publica todos os pontos válidos.
- Cada strategy rejeita valores ausentes/antigos; a janela atual das strategies concretas é 500 ms.
- O frontend apenas desenha o `chartPoint` recebido.

## Frontend e testes

`frontend_teste/index.html` usa SignalR 8.0.7 e, quando executado via Live Server (`127.0.0.1:5500`), usa URL absoluta `http://localhost:5000/hubs/telemetry`. CORS está permissivo para desenvolvimento.

Não abrir o HTML com `file://`. Alternativas:

- Live Server + URL absoluta + CORS;
- ou mover/servir a página por `wwwroot` e acessar `http://localhost:5000/...`, usando URL relativa `/hubs/telemetry`.

O mock já validou conexão, descoberta, assinatura, cinco `rawReading`, `1AF8 ? 1726 rpm`, atualização do store e eventos SignalR.

## Pendências para iniciar a próxima etapa

1. Confirmar/corrigir a declaração de `PidKey` e executar uma compilação limpa.
2. Criar testes automatizados para `PidDecoder`, `LatestValueStore`, `TelemetryHub` e strategies.
3. Formalizar `DeviceMessage` em arquivo próprio e mapear JSON explicitamente.
4. Adicionar validação de tamanho/paridade hexadecimal e `ResponseBytes` no decoder.
5. Implementar catálogo/configuração (`catalog`/`charts`) para o frontend.
6. Implementar desconexão do ESP32 e publicar `deviceStatus` também para o dispositivo.
7. Implementar `IDeviceAdapter`, `MockAdapter` e a migração de `ESP32Service`.
8. Definir regras formais de combinação de eixos e `MaxAgeMs` por eixo.
9. Definir suporte a múltiplos dispositivos; o estado atual suporta um socket e um conjunto de PIDs.
10. Integrar persistência histórica em MongoDB posteriormente.

## Ordem de trabalho

1. Corrigir/compilar o estado atual.
2. Criar testes unitários.
3. Estabilizar DTOs e contrato JSON.
4. Completar SignalR/configuração do frontend.
5. Implementar Adapter e MockAdapter.
6. Integrar ELM327 real.
7. Testar no carro, multi-PID e fallback.

Não alterar arquivos fora do escopo solicitado. Antes de escrever lógica central, explicar o fluxo e confirmar regras ambíguas.

Essa versão do README.md mantém a estrutura original e organiza as informações de forma clara e didática, facilitando a compreensão do projeto e suas funcionalidades.