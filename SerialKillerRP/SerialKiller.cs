/*  ============================================================================
 *  SERIAL KILLER RP  -  GTA V Story Mode (ScriptHookVDotNet v3)
 *  ----------------------------------------------------------------------------
 *  Mod de RP de serial killer para gravacao de video. Tudo no Numpad 1 a 9.
 *
 *    Numpad 1 -> Pega / solta o SACO DE LIXO (anda normalmente segurando)
 *    Numpad 2 -> Joga o saco que voce segura no PORTA-MALA mais proximo
 *    Numpad 3 -> AMARRA / desamarra o NPC mais proximo
 *    Numpad 4 -> CARREGA / larga o NPC (nas costas)
 *    Numpad 5 -> Coloca o NPC que voce carrega dentro do PORTA-MALA
 *    Numpad 6 -> EXECUCAO com faca no NPC mais proximo (mata)
 *    Numpad 7 -> ARRASTA / solta o corpo mais proximo
 *    Numpad 8 -> CAVAR / enterrar (pega uma pa e faz a animacao)
 *    Numpad 9 -> PANICO: cancela e solta tudo (tecla de seguranca)
 *
 *  As teclas e distancias podem ser trocadas no arquivo SerialKiller.ini
 *  (fica na mesma pasta scripts). Nao precisa recompilar nada.
 *  ============================================================================
 */

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class SerialKiller : Script
{
    // -------------------- animacoes (dict, nome, flag) -----------------------
    private struct Anim { public string Dict, Name; public int Flag;
        public Anim(string d, string n, int f) { Dict = d; Name = n; Flag = f; } }

    private static readonly Anim TIED     = new Anim("mp_arresting", "idle", 49);
    private static readonly Anim CARRY_ME = new Anim("missfinale_c2mcs_1", "fin_c2_mcs_1_camman", 49);
    private static readonly Anim CARRY_HIM= new Anim("nm@hands", "hands_up", 49);
    private static readonly Anim KNIFE    = new Anim("melee@knife@streamed_core", "plyr_takedown_front_slice", 0);
    private static readonly Anim DRAG_BODY= new Anim("combat@damage@writhe", "writhe_loop", 49);
    private static readonly Anim DIG      = new Anim("amb@world_human_gardener_plant@male@base", "base", 49);

    // -------------------- config (lido do .ini) ------------------------------
    private Keys kBag, kBagTrunk, kTie, kCarry, kVicTrunk, kKnife, kDrag, kDig, kPanic;
    private float interactDist = 2.5f;
    private float vehicleDist  = 3.5f;
    private string bagModel   = "prop_cs_rub_binbag_01";
    private string bagClipset = "anim@heists@box_carry@";
    private string shovelModel= "prop_tool_shovel";
    private bool showHelp = true;

    // -------------------- estado ---------------------------------------------
    private Prop bagProp;
    private bool holdingBag;
    private Prop shovelProp;
    private bool digging;
    private readonly List<Ped> tiedPeds = new List<Ped>();
    private Ped carrying;
    private Ped dragging;

    public SerialKiller()
    {
        LoadConfig();
        Tick    += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;   // limpa tudo se o mod for recarregado
        Interval = 0;
    }

    // =========================================================================
    //  CONFIG
    // =========================================================================
    private void LoadConfig()
    {
        ScriptSettings s = ScriptSettings.Load("scripts\\SerialKiller.ini");

        kBag      = ParseKey(s.GetValue("Keys", "BagToggle",     "NumPad1"), Keys.NumPad1);
        kBagTrunk = ParseKey(s.GetValue("Keys", "BagToTrunk",    "NumPad2"), Keys.NumPad2);
        kTie      = ParseKey(s.GetValue("Keys", "TieToggle",     "NumPad3"), Keys.NumPad3);
        kCarry    = ParseKey(s.GetValue("Keys", "CarryToggle",   "NumPad4"), Keys.NumPad4);
        kVicTrunk = ParseKey(s.GetValue("Keys", "VictimToTrunk", "NumPad5"), Keys.NumPad5);
        kKnife    = ParseKey(s.GetValue("Keys", "KnifeKill",     "NumPad6"), Keys.NumPad6);
        kDrag     = ParseKey(s.GetValue("Keys", "DragToggle",    "NumPad7"), Keys.NumPad7);
        kDig      = ParseKey(s.GetValue("Keys", "DigToggle",     "NumPad8"), Keys.NumPad8);
        kPanic    = ParseKey(s.GetValue("Keys", "PanicReset",    "NumPad9"), Keys.NumPad9);

        interactDist = s.GetValue("Settings", "InteractDistance", 2.5f);
        vehicleDist  = s.GetValue("Settings", "VehicleDistance",  3.5f);
        bagModel     = s.GetValue("Settings", "BagModel",   "prop_cs_rub_binbag_01");
        bagClipset   = s.GetValue("Settings", "BagClipset", "anim@heists@box_carry@");
        shovelModel  = s.GetValue("Settings", "ShovelModel","prop_tool_shovel");
        showHelp     = s.GetValue("Settings", "ShowHelpUI", true);
    }

    private Keys ParseKey(string val, Keys fallback)
    {
        try { return (Keys)Enum.Parse(typeof(Keys), val, true); }
        catch { return fallback; }
    }

    // =========================================================================
    //  INPUT
    // =========================================================================
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if      (e.KeyCode == kBag)      ToggleBag();
        else if (e.KeyCode == kBagTrunk) BagToTrunk();
        else if (e.KeyCode == kTie)      ToggleTie();
        else if (e.KeyCode == kCarry)    ToggleCarry();
        else if (e.KeyCode == kVicTrunk) VictimToTrunk();
        else if (e.KeyCode == kKnife)    KnifeKill();
        else if (e.KeyCode == kDrag)     ToggleDrag();
        else if (e.KeyCode == kDig)      ToggleDig();
        else if (e.KeyCode == kPanic)    PanicReset();
    }

    // =========================================================================
    //  TICK  (manutencao dos estados + UI)
    // =========================================================================
    private void OnTick(object sender, EventArgs e)
    {
        // mantem os amarrados "domados" e limpa refs mortas/inexistentes
        for (int i = tiedPeds.Count - 1; i >= 0; i--)
        {
            Ped p = tiedPeds[i];
            if (p == null || !p.Exists()) { tiedPeds.RemoveAt(i); continue; }
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, p, false);
        }

        if (showHelp) DrawHelp();
    }

    // =========================================================================
    //  1) SACO DE LIXO
    // =========================================================================
    private void ToggleBag()
    {
        if (holdingBag) DropBag();
        else            PickUpBag();
    }

    private void PickUpBag()
    {
        Ped player = Game.Player.Character;
        Model m = new Model(bagModel);
        m.Request(1000);
        if (!m.IsLoaded) { Notify("~r~Falha ao carregar o prop do saco."); return; }

        bagProp = World.CreateProp(m, player.Position, false, false);
        m.MarkAsNoLongerNeeded();
        if (bagProp == null) { Notify("~r~Nao consegui criar o saco."); return; }

        // prende na mao direita
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player, 57005 /*SKEL_R_Hand*/);
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bagProp, player, bone,
            0.12f, 0.02f, -0.03f, 0.0f, 90.0f, 0.0f,
            true, true, false, true, 1, true);

        // andar normalmente segurando algo (clipset de movimento)
        Function.Call(Hash.REQUEST_CLIP_SET, bagClipset);
        int t = 0;
        while (!Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, bagClipset) && t < 100) { Wait(0); t++; }
        Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, player, bagClipset, 1.0f);

        holdingBag = true;
        Notify("~g~Voce pegou o saco.~s~ Ande normalmente. (solta com a mesma tecla)");
    }

    private void DropBag()
    {
        Ped player = Game.Player.Character;
        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);

        if (bagProp != null && bagProp.Exists())
        {
            bagProp.Detach();
            Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, bagProp);
            bagProp.IsPositionFrozen = true;
        }
        holdingBag = false;
        Notify("~y~Voce soltou o saco.");
    }

    // =========================================================================
    //  2) SACO -> PORTA-MALA
    // =========================================================================
    private void BagToTrunk()
    {
        if (!holdingBag || bagProp == null || !bagProp.Exists())
        { Notify("~r~Voce nao esta segurando nenhum saco."); return; }

        Vehicle veh = World.GetClosestVehicle(Game.Player.Character.Position, vehicleDist);
        if (veh == null) { Notify("~r~Nenhum veiculo por perto."); return; }

        Ped player = Game.Player.Character;
        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);
        holdingBag = false;

        OpenTrunk(veh);
        Wait(900);

        bagProp.Detach();
        int boot = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, veh, "boot");
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bagProp, veh, boot,
            0.0f, 0.0f, 0.35f, 0.0f, 0.0f, 0.0f,
            true, true, false, false, 1, true);

        Wait(700);
        CloseTrunk(veh);
        Notify("~g~Saco guardado no porta-mala.");
    }

    // =========================================================================
    //  3) AMARRAR / DESAMARRAR
    // =========================================================================
    private void ToggleTie()
    {
        Ped ped = ClosestPed();
        if (ped == null) { Notify("~r~Nenhum NPC por perto."); return; }

        if (tiedPeds.Contains(ped))
        {
            tiedPeds.Remove(ped);
            ped.Task.ClearAll();
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, false);
            Function.Call(Hash.SET_ENABLE_HANDCUFFS, ped, false);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, true);
            Notify("~y~NPC desamarrado.");
            return;
        }

        Subdue(ped);
        Function.Call(Hash.SET_ENABLE_HANDCUFFS, ped, true);
        PlayAnim(ped, TIED, -1);
        tiedPeds.Add(ped);
        Notify("~g~NPC amarrado.~s~ (Numpad " + KeyNum(kCarry) + " pra carregar)");
    }

    // =========================================================================
    //  4) CARREGAR / LARGAR
    // =========================================================================
    private void ToggleCarry()
    {
        Ped player = Game.Player.Character;

        if (carrying != null && carrying.Exists())
        {
            Ped victim = carrying;
            victim.Detach();
            victim.Task.ClearAll();
            player.Task.ClearAll();
            if (tiedPeds.Contains(victim)) PlayAnim(victim, TIED, -1);
            carrying = null;
            Notify("~y~Voce largou o NPC.");
            return;
        }

        Ped v = ClosestPed();
        if (v == null) { Notify("~r~Nenhum NPC por perto."); return; }

        Subdue(v);
        PlayAnim(player, CARRY_ME, -1);
        PlayAnim(v, CARRY_HIM, -1);

        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, v, player, 0,
            0.27f, 0.15f, 0.63f, 0.5f, 0.5f, 0.0f,
            false, false, false, false, 2, true);

        carrying = v;
        Notify("~g~Carregando o NPC.~s~ (Numpad " + KeyNum(kVicTrunk) + " pra por no porta-mala)");
    }

    // =========================================================================
    //  5) NPC -> PORTA-MALA
    // =========================================================================
    private void VictimToTrunk()
    {
        if (carrying == null || !carrying.Exists())
        { Notify("~r~Voce nao esta carregando ninguem."); return; }

        Vehicle veh = World.GetClosestVehicle(Game.Player.Character.Position, vehicleDist);
        if (veh == null) { Notify("~r~Nenhum veiculo por perto."); return; }

        Ped victim = carrying;
        OpenTrunk(veh);
        Wait(900);

        victim.Detach();
        Game.Player.Character.Task.ClearAll();

        int boot = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, veh, "boot");
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, victim, veh, boot,
            0.0f, 0.2f, 0.25f, 90.0f, 0.0f, 90.0f,
            true, true, false, false, 2, true);

        PlayAnim(victim, DRAG_BODY, -1);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, victim, true); // nao "some" preso

        Wait(700);
        CloseTrunk(veh);
        carrying = null;
        Notify("~g~NPC guardado no porta-mala.");
    }

    // =========================================================================
    //  6) EXECUCAO COM FACA
    // =========================================================================
    private void KnifeKill()
    {
        Ped player = Game.Player.Character;
        Ped victim = ClosestPed();
        if (victim == null) { Notify("~r~Nenhum NPC por perto."); return; }

        Subdue(victim);
        Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, player, victim, 800);
        Wait(400);

        PlayAnim(player, KNIFE, 0);
        Wait(1200);

        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, victim, "BigHitByVehicle", 0.0f, 1.0f);
        victim.Kill();
        Notify("~r~...");
    }

    // =========================================================================
    //  7) ARRASTAR CORPO
    // =========================================================================
    private void ToggleDrag()
    {
        Ped player = Game.Player.Character;

        if (dragging != null && dragging.Exists())
        {
            dragging.Detach();
            dragging.Task.ClearAll();
            dragging = null;
            Notify("~y~Voce soltou o corpo.");
            return;
        }

        Ped v = ClosestPed();
        if (v == null) { Notify("~r~Nenhum corpo por perto."); return; }

        Function.Call(Hash.SET_ENTITY_INVINCIBLE, v, true);
        PlayAnim(v, DRAG_BODY, -1);

        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, v, player, 0,
            0.0f, -0.65f, -0.9f, 90.0f, 0.0f, 0.0f,
            false, false, false, false, 2, true);

        dragging = v;
        Notify("~g~Arrastando o corpo.~s~ (mesma tecla pra soltar)");
    }

    // =========================================================================
    //  8) CAVAR / ENTERRAR
    // =========================================================================
    private void ToggleDig()
    {
        Ped player = Game.Player.Character;

        if (digging)
        {
            player.Task.ClearAll();
            if (shovelProp != null && shovelProp.Exists()) shovelProp.Delete();
            digging = false;
            Notify("~y~Parou de cavar.");
            return;
        }

        Model m = new Model(shovelModel);
        m.Request(1000);
        if (m.IsLoaded)
        {
            shovelProp = World.CreateProp(m, player.Position, false, false);
            m.MarkAsNoLongerNeeded();
            int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player, 57005 /*SKEL_R_Hand*/);
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, shovelProp, player, bone,
                0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, true, true, false, true, 1, true);
        }

        PlayAnim(player, DIG, -1);
        digging = true;
        Notify("~g~Cavando...~s~ (mesma tecla pra parar)");
    }

    // =========================================================================
    //  9) PANICO - solta tudo
    // =========================================================================
    private void PanicReset()
    {
        Ped player = Game.Player.Character;
        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);
        player.Task.ClearAll();

        if (bagProp != null && bagProp.Exists()) { bagProp.Detach(); bagProp.Delete(); }
        holdingBag = false;

        if (shovelProp != null && shovelProp.Exists()) shovelProp.Delete();
        digging = false;

        if (carrying != null && carrying.Exists()) { carrying.Detach(); carrying.Task.ClearAll(); }
        carrying = null;

        if (dragging != null && dragging.Exists()) { dragging.Detach(); dragging.Task.ClearAll(); }
        dragging = null;

        foreach (Ped p in tiedPeds)
        {
            if (p != null && p.Exists())
            {
                p.Task.ClearAll();
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
                Function.Call(Hash.SET_ENABLE_HANDCUFFS, p, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, p, true);
            }
        }
        tiedPeds.Clear();
        Notify("~b~Reset feito. Tudo solto.");
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================
    private void OnAborted(object sender, EventArgs e)
    {
        // limpeza minima quando o mod eh recarregado (Insert)
        try { PanicReset(); } catch { }
    }

    private Ped ClosestPed()
    {
        Ped player = Game.Player.Character;
        Ped best = null;
        float bestDist = interactDist;
        foreach (Ped p in World.GetNearbyPeds(player, interactDist))
        {
            if (p == null || p == player || p.IsPlayer) continue;
            float d = player.Position.DistanceTo(p.Position);
            if (d < bestDist) { best = p; bestDist = d; }
        }
        return best;
    }

    private void Subdue(Ped ped)
    {
        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, ped, true, true);
        ped.Task.ClearAllImmediately();
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, false);
        Function.Call(Hash.SET_PED_KEEP_TASK, ped, true);
    }

    private void PlayAnim(Ped ped, Anim a, int duration)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, a.Dict);
        int t = 0;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, a.Dict) && t < 100) { Wait(0); t++; }
        Function.Call(Hash.TASK_PLAY_ANIM, ped, a.Dict, a.Name,
            8.0f, -8.0f, duration, a.Flag, 0.0f, false, false, false);
    }

    private void OpenTrunk(Vehicle veh)
    {
        Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, veh, 5, false, false);
    }
    private void CloseTrunk(Vehicle veh)
    {
        Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, veh, 5, false);
    }

    private void Notify(string msg)
    {
        Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, msg);
        Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, true);
    }

    private string KeyNum(Keys k)
    {
        string s = k.ToString().Replace("NumPad", "");
        return s;
    }

    // Lista de teclas desenhada no canto (pode desligar no .ini)
    private void DrawHelp()
    {
        string[] lines = {
            "~p~SERIAL KILLER RP",
            "~s~" + KeyNum(kBag)      + " Saco (pega/solta)",
            "~s~" + KeyNum(kBagTrunk) + " Saco -> porta-mala",
            "~s~" + KeyNum(kTie)      + " Amarrar NPC",
            "~s~" + KeyNum(kCarry)    + " Carregar NPC",
            "~s~" + KeyNum(kVicTrunk) + " NPC -> porta-mala",
            "~s~" + KeyNum(kKnife)    + " Faca (matar)",
            "~s~" + KeyNum(kDrag)     + " Arrastar corpo",
            "~s~" + KeyNum(kDig)      + " Cavar",
            "~s~" + KeyNum(kPanic)    + " PANICO (reset)"
        };

        float y = 0.28f;
        for (int i = 0; i < lines.Length; i++)
        {
            DrawText(lines[i], 0.015f, y, 0.30f);
            y += 0.022f;
        }
    }

    private void DrawText(string text, float x, float y, float scale)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 200);
        Function.Call(Hash.SET_TEXT_OUTLINE);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
