# Serial Killer RP — GTA V (Modo História / Single Player)

Mod de RP de serial killer feito pra gravação de vídeo. Todas as ações ficam no
**Numpad 1 a 9**. Funciona no **modo história** do GTA V (não é FiveM).

---

## O que cada tecla faz

| Tecla | Ação |
|-------|------|
| **Numpad 1** | Pega / solta o **saco de lixo** (você anda normalmente segurando) |
| **Numpad 2** | Joga o saco que você segura no **porta-mala** do carro mais próximo |
| **Numpad 3** | **Amarra / desamarra** o NPC mais próximo |
| **Numpad 4** | **Carrega / larga** o NPC (nas costas) |
| **Numpad 5** | Coloca o NPC que você carrega **dentro do porta-mala** |
| **Numpad 6** | **Execução com faca** no NPC mais próximo (mata) |
| **Numpad 7** | **Arrasta / solta** o corpo mais próximo |
| **Numpad 8** | **Cavar / enterrar** (pega uma pá e faz a animação) |
| **Numpad 9** | **PÂNICO** — cancela e solta tudo (tecla de segurança) |

O fluxo de RP fica: `Numpad 3` amarrar → `Numpad 6` matar (ou `Numpad 4` carregar
vivo) → `Numpad 1` saco → `Numpad 5`/`Numpad 2` porta-mala → dirigir → `Numpad 7`
arrastar → `Numpad 8` enterrar.

Você pode trocar qualquer tecla no arquivo **`SerialKiller.ini`** (não precisa
recompilar nada).

---

## Pré-requisitos (instale nesta ordem)

1. **Script Hook V** — do Alexander Blade (dev-c.com). Copie `ScriptHookV.dll`
   e `dinput8.dll` pra pasta raiz do GTA V.
2. **Script Hook V .NET (SHVDN v3)** — do GitHub `crosire/scripthookvdotnet`
   (release **v3**). Copie `ScriptHookVDotNet.asi`,
   `ScriptHookVDotNet3.dll` e `ScriptHookVDotNet.ini` pra pasta raiz do GTA V.
3. **.NET Framework 4.8** (o Windows 10/11 já vem com ele).

> Dica: se o jogo tiver atualizado recentemente, confirme que a versão do
> Script Hook V é compatível com a build atual do GTA V, senão os mods não
> carregam.

---

## Instalação do mod

1. Crie a pasta **`scripts`** dentro da pasta raiz do GTA V, se ainda não existir:
   ```
   ...\Grand Theft Auto V\scripts\
   ```
2. Copie estes **2 arquivos** pra dentro de `scripts\`:
   - `SerialKiller.cs`
   - `SerialKiller.ini`
3. Pronto. Abra o jogo. O SHVDN v3 **compila o `.cs` sozinho** quando o jogo
   carrega — você não precisa compilar nada.

> Recarregar sem fechar o jogo: aperte **Insert** (recarrega todos os scripts do
> SHVDN). Útil se você editar o `.ini` ou o `.cs`.

---

## Ajustes rápidos (`SerialKiller.ini`)

- **Teclas**: troque em `[Keys]`. Aceita `NumPad0..NumPad9`, `F1..F12`, letras, etc.
- **Distância de pegar NPC** / **de achar carro**: `[Settings]`.
- **`BagClipset`**: se você tiver uma animação custom `_bag_walk_garbage_man`
  instalada, coloque o nome do clipset dela aqui. Por padrão usa
  `anim@heists@box_carry@` (do jogo base — anda segurando com as duas mãos).
- **`ShowHelpUI`**: `true`/`false` pra mostrar/esconder a lista de teclas na tela.

---

## Observações / solução de problemas

- **Nada acontece ao apertar as teclas** → confirme que o Script Hook V e o
  SHVDN v3 estão instalados e compatíveis com a build do jogo. Veja o arquivo
  `ScriptHookVDotNet.log` na raiz do GTA (ele mostra erros de compilação do `.cs`).
- **NPC foge / reage** → chegue **bem perto e por trás** antes de apertar amarrar.
  O `Numpad 9` (pânico) reseta tudo se algo travar.
- **Animação estranha em algum carro** → a posição dentro do porta-mala varia de
  modelo pra modelo; carros com porta-mala grande (Tailgater, Baller) ficam melhores.
- **Prop do saco não aparece na mão certa** → ajuste os offsets no código (função
  `PickUpBag`) ou o `BagModel` no `.ini`.

---

## Ideias pra deixar o RP ainda melhor

Sugestões (algumas dá pra eu já implementar se quiser):

1. **Sangue e faca na mão de verdade** — dar a arma `WEAPON_KNIFE` ao apertar
   Numpad 6 e aplicar decal de sangue no chão/roupa.
2. **Limpar vestígios** — uma tecla com animação de esfregão pra "limpar o sangue"
   depois do crime.
3. **Modo furtivo** — reduzir o áudio dos passos e deixar NPCs menos atentos
   enquanto você segura o saco/corpo.
4. **Cova que "engole" o corpo** — depois de cavar (Numpad 8), o corpo mais
   próximo afunda no chão e some (enterrado de verdade).
5. **Amordaçar / vendar** — prop de fita/venda no NPC amarrado, com animação de
   medo em loop pra ficar mais dramático.
6. **Marcador de "cena do crime"** — um blip no mapa onde você deixou um corpo,
   pra facilitar a gravação/edição.
7. **Câmera de execução** — travar uma câmera cinemática por 2-3s no momento da
   morte (bom pra corte no vídeo).
8. **Serial signature** — deixar um "objeto assinatura" (ex.: uma carta/prop) ao
   lado da vítima, coisa clássica de filme de serial killer.

Me fala quais dessas você quer que eu adicione.
