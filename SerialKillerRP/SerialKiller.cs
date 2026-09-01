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
    // carregar: camman = quem carrega (secondary=49, nao trava o andar),
    // greg = o corpo carregado (full-body=1, fica sempre posado no ombro).
    private static readonly Anim CARRY_ME = new Anim("missfinale_c2mcs_1", "fin_c2_mcs_1_camman", 49);
    private static readonly Anim CARRY_HIM= new Anim("missfinale_c2mcs_1", "fin_c2_mcs_1_greg", 1);
    private static readonly Anim KNIFE    = new Anim("melee@knife@streamed_core", "plyr_takedown_front_slice", 0);
    private static readonly Anim DRAG_BODY= new Anim("combat@damage@writhe", "writhe_loop", 1);
    private static readonly Anim DIG      = new Anim("amb@world_human_gardener_plant@male@base", "base", 49);

    // gestos do killer (voce se abaixa / faz o movimento)
    // ajoelhar no chao pra amarrar (anim ancorada ao chao, nao flutua)
    private static readonly Anim SEARCH   = new Anim("amb@medic@standing@kneel@base", "base", 1);
    private static readonly Anim PICKUP   = new Anim("pickup_object", "pickup_low", 0);   // abaixar pra pegar
    private static readonly Anim PUTDOWN  = new Anim("pickup_object", "putdown_low", 0);  // abaixar pra soltar
    // pegar/soltar corpo (metodo do LosSantosSerialKiller - fica ancorado ao chao)
    private static readonly Anim SNOW_PICK = new Anim("anim@mp_snowball", "pickup_snowball", 0);
    // carregar corpo: box_carry idle como SECONDARY (flag 49) = nao trava o andar.
    // re-aplicada todo tick pelo MaintainCarry (jeito do mod de referencia).
    private static readonly Anim CARRY_BODY = new Anim("anim@heists@box_carry@", "idle", 49);
    private static readonly Anim MASK     = new Anim("mp_masks@on_foot", "put_on_mask", 0);            // colocar mascara
    private static readonly Anim CLEAN    = new Anim("timetable@floyd@clean_kitchen@base", "base", 1); // limpar vestigios

    // -------------------- config (lido do .ini) ------------------------------
    private Keys kBag, kBagTrunk, kTie, kCarry, kVicTrunk, kKnife, kDrag, kDig, kPanic, kMask, kClean, kHud;
    private bool showNotifications = false;   // clean: nenhuma notificacao por padrao
    private float interactDist = 2.5f;
    private float vehicleDist  = 3.5f;
    private string bagModel   = "prop_rub_binbag_01";
    private string bagClipset = "anim@heists@box_carry@";
    private string shovelModel= "prop_tool_shovel";
    private bool showHelp = true;
    private bool cinematicKill = true;

    // offsets ajustaveis (edite no .ini e aperte Insert pra recarregar)
    private float bagOffX = 0.12f, bagOffY = 0.02f, bagOffZ = -0.03f;
    private float bagRotX = 0f,    bagRotY = 90f,   bagRotZ = 0f;
    private float carOffX = 0.27f, carOffY = 0.16f, carOffZ = 0.63f;
    private float carRotX = 0f,    carRotY = 0f,    carRotZ = 0f;
    private float dragOffX= 0.0f,  dragOffY=-0.85f, dragOffZ=-0.55f;
    private float dragRotX= 0f,    dragRotY= 90f,   dragRotZ= 0f;

    // corpo embrulhado (metodo do LosSantosSerialKiller: carrega um PROP, nao o ped)
    private string corpseModel = "prop_water_corpse_02";
    private bool   wrapFade = true;
    private float corpOffX=-0.08f, corpOffY=0.22f, corpOffZ=-0.18f;
    private float corpRotX=0f,     corpRotY=45f,   corpRotZ=90f;
    private int   corpBone=11816;  // CarryBone do mod de referencia

    // sistema de evidencias / suspeita
    private bool showEvidence = true;
    private int  maskDrawable = 5;   // varia por modelo de ped (veja README)
    private int  maskTexture  = 0;
    private float killHeat    = 20f;
    private float buryHeat    = 15f;
    private float cleanHeat   = 12f;
    private float witnessHeat = 8f;

    // -------------------- estado ---------------------------------------------
    private Prop bagProp;
    private bool holdingBag;
    private Prop shovelProp;
    private bool digging;
    private readonly List<Ped> tiedPeds = new List<Ped>();
    private Ped dragging;

    // corpo embrulhado sendo carregado (prop, nao ped)
    private Prop bodyProp;
    private bool carryingBody;

    private bool masked;
    private int  prevMaskDrawable, prevMaskTexture;
    private float heat;   // 0 a 100 (medidor de suspeita)

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
        kMask     = ParseKey(s.GetValue("Keys", "MaskToggle",    "NumPad0"), Keys.NumPad0);
        kClean    = ParseKey(s.GetValue("Keys", "CleanEvidence", "Decimal"), Keys.Decimal);
        kHud      = ParseKey(s.GetValue("Keys", "ToggleHud",     "Subtract"), Keys.Subtract);

        interactDist = s.GetValue("Settings", "InteractDistance", 2.5f);
        vehicleDist  = s.GetValue("Settings", "VehicleDistance",  3.5f);
        bagModel     = s.GetValue("Settings", "BagModel",   "prop_rub_binbag_01");
        bagClipset   = s.GetValue("Settings", "BagClipset", "anim@heists@box_carry@");
        shovelModel  = s.GetValue("Settings", "ShovelModel","prop_tool_shovel");
        showHelp     = s.GetValue("Settings", "ShowHelpUI", true);
        cinematicKill= s.GetValue("Settings", "CinematicKill", true);
        showNotifications = s.GetValue("Settings", "ShowNotifications", false);

        bagOffX = s.GetValue("Offsets", "BagOffX", 0.12f); bagOffY = s.GetValue("Offsets", "BagOffY", 0.02f); bagOffZ = s.GetValue("Offsets", "BagOffZ", -0.03f);
        bagRotX = s.GetValue("Offsets", "BagRotX", 0f);    bagRotY = s.GetValue("Offsets", "BagRotY", 90f);   bagRotZ = s.GetValue("Offsets", "BagRotZ", 0f);
        carOffX = s.GetValue("Offsets", "CarryOffX", 0.27f); carOffY = s.GetValue("Offsets", "CarryOffY", 0.16f); carOffZ = s.GetValue("Offsets", "CarryOffZ", 0.63f);
        carRotX = s.GetValue("Offsets", "CarryRotX", 0f);    carRotY = s.GetValue("Offsets", "CarryRotY", 0f);     carRotZ = s.GetValue("Offsets", "CarryRotZ", 0f);
        dragOffX= s.GetValue("Offsets", "DragOffX", 0.0f);   dragOffY= s.GetValue("Offsets", "DragOffY", -0.85f);  dragOffZ= s.GetValue("Offsets", "DragOffZ", -0.55f);
        dragRotX= s.GetValue("Offsets", "DragRotX", 0f);     dragRotY= s.GetValue("Offsets", "DragRotY", 90f);     dragRotZ= s.GetValue("Offsets", "DragRotZ", 0f);

        corpseModel = s.GetValue("Body", "ModelName", "prop_water_corpse_02");
        wrapFade    = s.GetValue("Body", "WrapFade", true);
        corpOffX = s.GetValue("Offsets", "CorpseOffX", -0.08f); corpOffY = s.GetValue("Offsets", "CorpseOffY", 0.22f); corpOffZ = s.GetValue("Offsets", "CorpseOffZ", -0.18f);
        corpRotX = s.GetValue("Offsets", "CorpseRotX", 0f);     corpRotY = s.GetValue("Offsets", "CorpseRotY", 45f);   corpRotZ = s.GetValue("Offsets", "CorpseRotZ", 90f);
        corpBone = s.GetValue("Offsets", "CorpseBone", 11816);

        showEvidence = s.GetValue("Evidence", "ShowSuspicionBar", true);
        maskDrawable = s.GetValue("Evidence", "MaskDrawable", 5);
        maskTexture  = s.GetValue("Evidence", "MaskTexture", 0);
        killHeat     = s.GetValue("Evidence", "KillHeat", 20f);
        buryHeat     = s.GetValue("Evidence", "BuryHeat", 15f);
        cleanHeat    = s.GetValue("Evidence", "CleanHeat", 12f);
        witnessHeat  = s.GetValue("Evidence", "WitnessHeat", 8f);
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
        else if (e.KeyCode == kMask)     ToggleMask();
        else if (e.KeyCode == kClean)    CleanEvidence();
        else if (e.KeyCode == kHud)      { showHelp = !showHelp; showEvidence = !showEvidence; }
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

        // mantem o corpo carregado estavel (re-aplica anim + attach)
        MaintainCarry();

        // suspeita esfria bem devagar quando voce nao faz nada
        if (heat > 0f) heat -= 0.02f;
        if (heat < 0f) heat = 0f;

        if (showHelp)     DrawHelp();
        if (showEvidence) DrawHeat();
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
        // tenta o modelo do ini e, se nao carregar, cai em props de mundo
        // que sempre existem (o "cs_" original eh de cutscene e falha no free).
        string[] candidates = { bagModel, "prop_rub_binbag_01", "prop_rub_binbag_sd_01", "prop_rub_binbag_03b", "prop_cs_rub_binbag_01" };
        Model m = new Model(0);
        bool loaded = false;
        foreach (string name in candidates)
        {
            m = new Model(name);
            if (!m.IsValid) continue;
            m.Request(1000);
            int mt = 0;
            while (!m.IsLoaded && mt < 60) { Wait(10); mt++; }
            if (m.IsLoaded) { loaded = true; break; }
        }
        if (!loaded) { Notify("~r~Nenhum modelo de saco carregou."); return; }

        bagProp = World.CreateProp(m, player.Position, false, false);
        m.MarkAsNoLongerNeeded();
        if (bagProp == null || !bagProp.Exists()) { Notify("~r~Nao consegui criar o saco."); return; }

        // prende na mao direita. IMPORTANTE: isPed=false (eh um PROP!) e vertex=2,
        // exatamente como o mod de referencia faz. Com isPed=true o attach falha.
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player, 57005 /*SKEL_R_Hand*/);
        Function.Call(Hash.DETACH_ENTITY, bagProp, false, false);
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bagProp, player, bone,
            bagOffX, bagOffY, bagOffZ, bagRotX, bagRotY, bagRotZ,
            false, false, false, false, 2, true);

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

        // abaixa pra "colocar" o saco no chao
        PlayAnimBlocking(player, PUTDOWN, 900);

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

        // guarda no porta-mala e SOME (fica invisivel dentro, sem colisao)
        bagProp.Detach();
        int boot = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, veh, "boot");
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bagProp, veh, boot,
            0.0f, -0.25f, -0.1f, 0.0f, 0.0f, 0.0f,
            true, true, false, false, 1, true);
        Function.Call(Hash.SET_ENTITY_COLLISION, bagProp, false, false);
        bagProp.IsVisible = false;

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
        // a vitima ja levanta as maos e voce se ajoelha no chao pra amarrar
        Function.Call(Hash.SET_ENABLE_HANDCUFFS, ped, true);
        PlayAnim(ped, TIED, -1);
        PlayAnimBlocking(Game.Player.Character, SEARCH, 2200);
        Game.Player.Character.Task.ClearAll();

        tiedPeds.Add(ped);
        Notify("~g~NPC amarrado.~s~ (Numpad " + KeyNum(kCarry) + " pra carregar)");
    }

    // =========================================================================
    //  4) CARREGAR / LARGAR
    // =========================================================================
    private void ToggleCarry()
    {
        Ped player = Game.Player.Character;

        // ---- ja carregando um corpo -> larga no chao ----
        if (carryingBody && bodyProp != null && bodyProp.Exists())
        {
            carryingBody = false;
            Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, player);
            PlayAnimBlocking(player, SNOW_PICK, 700);
            bodyProp.Detach();
            Function.Call(Hash.SET_ENTITY_COLLISION, bodyProp, true, true);
            Function.Call(Hash.PLACE_OBJECT_ON_GROUND_PROPERLY, bodyProp);
            bodyProp.IsPositionFrozen = true;
            Notify("~y~Voce largou o corpo.");
            return;
        }

        // ---- achar vitima, embrulhar num PROP e carregar ----
        Ped v = ClosestPed();
        if (v == null) { Notify("~r~Nenhum NPC por perto."); return; }

        // ajoelha (anim ancorada ao chao)
        PlayAnimBlocking(player, SEARCH, 1500);

        Vector3 pos = v.Position;

        // fade preto pra trocar o ped vivo pelo corpo-prop sem glitch
        if (wrapFade) { Function.Call(Hash.DO_SCREEN_FADE_OUT, 500); Wait(650); }

        tiedPeds.Remove(v);
        if (v.Exists()) v.Delete();

        Model cm = LoadFirstModel(new string[] { corpseModel, "prop_water_corpse_02", "prop_rub_binbag_01" });
        if (cm.Hash == 0) { if (wrapFade) Function.Call(Hash.DO_SCREEN_FADE_IN, 400); Notify("~r~Falha ao criar o corpo."); return; }

        bodyProp = World.CreateProp(cm, pos, false, false);
        cm.MarkAsNoLongerNeeded();
        if (bodyProp == null || !bodyProp.Exists()) { if (wrapFade) Function.Call(Hash.DO_SCREEN_FADE_IN, 400); return; }

        // prende na mao (bone 11816 = mesmo do mod de referencia)
        Function.Call(Hash.SET_ENTITY_COLLISION, bodyProp, false, false);
        AttachBodyToHand(player);

        // box_carry idle como secondary (nao trava andar). MaintainCarry re-aplica.
        PlayAnim(player, CARRY_BODY, -1);

        if (wrapFade) Function.Call(Hash.DO_SCREEN_FADE_IN, 600);
        carryingBody = true;
        Notify("~g~Carregando o corpo.~s~ (Numpad " + KeyNum(kVicTrunk) + " pra por no porta-mala)");
    }

    // =========================================================================
    //  5) CORPO -> PORTA-MALA
    // =========================================================================
    private void VictimToTrunk()
    {
        if (!carryingBody || bodyProp == null || !bodyProp.Exists())
        { Notify("~r~Voce nao esta carregando nenhum corpo."); return; }

        Vehicle veh = World.GetClosestVehicle(Game.Player.Character.Position, vehicleDist);
        if (veh == null) { Notify("~r~Nenhum veiculo por perto."); return; }

        Ped player = Game.Player.Character;
        carryingBody = false;
        Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, player);

        OpenTrunk(veh);
        Wait(900);

        bodyProp.Detach();
        int boot = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, veh, "boot");
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bodyProp, veh, boot,
            0.0f, -0.3f, -0.2f, 0.0f, 0.0f, 0.0f,
            true, true, false, false, 1, true);
        Function.Call(Hash.SET_ENTITY_COLLISION, bodyProp, false, false);
        bodyProp.IsVisible = false;

        Wait(700);
        CloseTrunk(veh);
        Notify("~g~Corpo guardado no porta-mala.");
    }

    // =========================================================================
    //  6) EXECUCAO COM FACA (faca na mao + sangue + camera cinematica)
    // =========================================================================
    private void KnifeKill()
    {
        Ped player = Game.Player.Character;
        Ped victim = ClosestPed();
        if (victim == null) { Notify("~r~Nenhum NPC por perto."); return; }

        Subdue(victim);

        // vira pra vitima direto (sem usar task, que atrapalha a anim)
        Vector3 dir = victim.Position - player.Position;
        player.Heading = Function.Call<float>(Hash.GET_HEADING_FROM_VECTOR_2D, dir.X, dir.Y);
        Wait(300);

        // camera cinematica focando a vitima
        Camera cam = null;
        if (cinematicKill)
        {
            Vector3 camPos = player.Position
                           + player.ForwardVector * 1.8f
                           + player.RightVector   * 1.2f
                           + new Vector3(0f, 0f, 0.35f);
            cam = World.CreateCamera(camPos, Vector3.Zero, 50f);
            cam.PointAt(victim);
            World.RenderingCamera = cam;
        }

        // TIRA qualquer arma da mao: uma arma equipada cria uma task que
        // sobrepoe a animacao (por isso o golpe nunca tocava). Sem arma, a
        // anim toca; a faca vira um PROP na mao so pro visual.
        Function.Call(Hash.SET_CURRENT_PED_WEAPON, player,
            Function.Call<int>(Hash.GET_HASH_KEY, "WEAPON_UNARMED"), true);
        Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player);

        Prop knife = null;
        Model km = new Model("w_me_knife");
        km.Request(1000);
        int kt = 0; while (!km.IsLoaded && kt < 100) { Wait(5); kt++; }
        if (km.IsLoaded)
        {
            knife = World.CreateProp(km, player.Position, false, false);
            km.MarkAsNoLongerNeeded();
            int hb = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player, 57005 /*SKEL_R_Hand*/);
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, knife, player, hb,
                0.09f, 0.03f, 0.0f, 0.0f, 0.0f, 0.0f, true, true, false, true, 1, true);
        }

        // agora a facada realmente toca (full-body, esperando terminar)
        PlayAnimBlocking(player, KNIFE, 1600);

        if (knife != null && knife.Exists()) knife.Delete();

        // sangue: na vitima e no chao
        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, victim, "BigHitByVehicle", 0.0f, 1.0f);
        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, victim, "SCR_Dumpster", 0.0f, 1.0f);
        BloodOnGround(victim.Position);
        victim.Kill();

        // sistema de evidencias: sobe a suspeita e checa testemunhas
        AddHeat(killHeat);
        CheckWitnesses(victim);

        Wait(1400);

        if (cam != null)
        {
            World.RenderingCamera = null;
            cam.Delete();
        }
        Notify("~r~...");
    }

    private void BloodOnGround(Vector3 pos)
    {
        // decal de poca de sangue no chao (cosmetico; timeout -1 = permanente)
        Function.Call(Hash.ADD_DECAL, 1023,
            pos.X, pos.Y, pos.Z,
            0f, 0f, -1f,   // direcao (pra baixo)
            1f, 0f, 0f,    // vetor lateral
            1.4f, 1.4f,    // largura, altura
            0.5f, 0.0f, 0.0f, 1.0f,  // cor RGBA (vermelho)
            -1f, false, false, false);
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

        // voce se abaixa e agarra o corpo antes de arrastar
        PlayAnimBlocking(player, PICKUP, 900);

        Function.Call(Hash.SET_ENTITY_INVINCIBLE, v, true);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, v, false);
        PlayAnim(v, DRAG_BODY, -1);

        // corpo preso atras de voce, no chao (offsets ajustaveis no .ini)
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, v, player, 0,
            dragOffX, dragOffY, dragOffZ, dragRotX, dragRotY, dragRotZ,
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

            // enterra de verdade: o corpo mais proximo afunda no chao e some
            BuryNearest();

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

    private void BuryNearest()
    {
        Ped player = Game.Player.Character;
        Ped body = null;
        float bestDist = interactDist * 2f;
        foreach (Ped p in World.GetNearbyPeds(player, interactDist * 2f))
        {
            if (p == null || p == player || p.IsPlayer) continue;
            float d = player.Position.DistanceTo(p.Position);
            if (d < bestDist) { body = p; bestDist = d; }
        }
        if (body == null) { Notify("~y~Nenhum corpo perto pra enterrar."); return; }

        // se estava sendo arrastado/amarrado, libera as refs
        if (dragging == body) { body.Detach(); dragging = null; }
        tiedPeds.Remove(body);

        Function.Call(Hash.SET_ENTITY_INVINCIBLE, body, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, body, false, false);

        // afunda no chao aos poucos e some
        Vector3 start = body.Position;
        for (int i = 1; i <= 20; i++)
        {
            body.PositionNoOffset = new Vector3(start.X, start.Y, start.Z - i * 0.08f);
            Wait(60);
        }
        body.Delete();
        AddHeat(-buryHeat);   // sumir com o corpo reduz a suspeita
        Notify("~g~Corpo enterrado.");
    }

    // =========================================================================
    //  MASCARA  (colocar / tirar, com animacao)
    // =========================================================================
    private void ToggleMask()
    {
        Ped player = Game.Player.Character;
        PlayAnimBlocking(player, MASK, 1300);   // gesto de puxar a mascara pro rosto

        if (!masked)
        {
            // guarda o visual atual pra poder voltar depois
            prevMaskDrawable = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player, 1);
            prevMaskTexture  = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player, 1);
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player, 1, maskDrawable, maskTexture, 0);
            player.Task.ClearAll();
            masked = true;
            Notify("~p~Mascara colocada.~s~ (se nao aparecer, veja MaskDrawable no .ini)");
        }
        else
        {
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player, 1, prevMaskDrawable, prevMaskTexture, 0);
            masked = false;
            Notify("~y~Mascara retirada.");
        }
    }

    // =========================================================================
    //  EVIDENCIAS  (limpar vestigios + medidor de suspeita)
    // =========================================================================
    private void CleanEvidence()
    {
        Ped player = Game.Player.Character;
        PlayAnimBlocking(player, CLEAN, 2600);   // esfrega o chao

        Vector3 pos = player.Position;
        Function.Call(Hash.REMOVE_DECALS_IN_RANGE, pos.X, pos.Y, pos.Z, 6.0f);
        player.Task.ClearAll();

        AddHeat(-cleanHeat);
        Notify("~g~Vestigios limpos.");
    }

    private void AddHeat(float amount)
    {
        heat += amount;
        if (heat < 0f)   heat = 0f;
        if (heat > 100f) heat = 100f;
        if (heat >= 100f) Escalate();
    }

    private void Escalate()
    {
        // suspeita no maximo: a policia vem atras
        Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, Game.Player, 3, false);
        Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player, false);
        Notify("~r~Suspeita no maximo! A policia foi acionada.");
    }

    // Testemunhas: NPCs que viram o crime fogem e aumentam a suspeita.
    // Se voce estiver de mascara, testemunham menos (nao te identificam).
    private void CheckWitnesses(Ped victim)
    {
        Ped player = Game.Player.Character;
        int witnesses = 0;

        foreach (Ped p in World.GetNearbyPeds(player, 30f))
        {
            if (p == null || p == player || p.IsPlayer || p == victim) continue;
            if (!p.IsAlive || tiedPeds.Contains(p)) continue;

            // tem linha de visao ate voce?
            if (Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, p, player, 17))
            {
                witnesses++;
                Function.Call(Hash.TASK_SMART_FLEE_PED, p, player, 120f, -1, false, false);
                Function.Call(Hash.SET_PED_KEEP_TASK, p, true);
            }
        }

        if (witnesses > 0)
        {
            float gain = witnessHeat * witnesses;
            if (masked) gain *= 0.4f;   // mascara = menos suspeita
            AddHeat(gain);
            Notify("~o~" + witnesses + " testemunha(s)!" + (masked ? " (mascara ajudou)" : ""));
        }
    }

    // =========================================================================
    //  9) PANICO - solta tudo
    // =========================================================================
    private void PanicReset()
    {
        Ped player = Game.Player.Character;
        Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);
        player.Task.ClearAll();

        // garante que a camera e o fade voltam ao normal
        World.RenderingCamera = null;
        World.DestroyAllCameras();
        Function.Call(Hash.DO_SCREEN_FADE_IN, 300);

        if (bagProp != null && bagProp.Exists()) { bagProp.Detach(); bagProp.Delete(); }
        holdingBag = false;

        if (shovelProp != null && shovelProp.Exists()) shovelProp.Delete();
        digging = false;

        if (bodyProp != null && bodyProp.Exists()) { bodyProp.Detach(); bodyProp.Delete(); }
        carryingBody = false;

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

        // tira a mascara e zera a suspeita
        if (masked)
        {
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player, 1, prevMaskDrawable, prevMaskTexture, 0);
            masked = false;
        }
        heat = 0f;

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

    // Toca a animacao E espera ela acontecer (ms). Usado pros gestos do killer.
    private void PlayAnimBlocking(Ped ped, Anim a, int ms)
    {
        PlayAnim(ped, a, ms < 0 ? -1 : ms);
        if (ms > 0) Wait(ms);
    }

    // Prende o corpo-prop na mao do player (bone/offsets do mod de referencia)
    private void AttachBodyToHand(Ped player)
    {
        if (bodyProp == null || !bodyProp.Exists()) return;
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, player, corpBone);
        // isPed=false (eh um PROP) e vertex=2 -> igual ao mod de referencia
        Function.Call(Hash.DETACH_ENTITY, bodyProp, false, false);
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, bodyProp, player, bone,
            corpOffX, corpOffY, corpOffZ, corpRotX, corpRotY, corpRotZ,
            false, false, false, false, 2, true);
    }

    // Mantem o corpo carregado estavel: re-aplica a anim e o attach todo tick
    // (jeito do MaintainCarryState do LosSantosSerialKiller).
    private void MaintainCarry()
    {
        if (!carryingBody) return;
        Ped player = Game.Player.Character;
        if (bodyProp == null || !bodyProp.Exists()) { carryingBody = false; return; }

        if (!Function.Call<bool>(Hash.IS_ENTITY_PLAYING_ANIM, player, CARRY_BODY.Dict, CARRY_BODY.Name, 3))
            PlayAnim(player, CARRY_BODY, -1);

        if (!Function.Call<bool>(Hash.IS_ENTITY_ATTACHED_TO_ENTITY, bodyProp, player))
            AttachBodyToHand(player);
    }

    // Tenta carregar o primeiro modelo valido da lista. Retorna Model (Hash==0 se falhou).
    private Model LoadFirstModel(string[] names)
    {
        foreach (string n in names)
        {
            Model m = new Model(n);
            if (!m.IsValid) continue;
            m.Request(1000);
            int t = 0;
            while (!m.IsLoaded && t < 60) { Wait(10); t++; }
            if (m.IsLoaded) return m;
        }
        return new Model(0);
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
        if (!showNotifications) return;   // clean: sem notificacoes
        Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, msg);
        Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, true);
    }

    private string KeyNum(Keys k)
    {
        string s = k.ToString().Replace("NumPad", "");
        return s;
    }

    // Nome amigavel pra teclas que nao sao numero (ex.: Decimal -> ".")
    private string KeyName(Keys k)
    {
        if (k == Keys.Decimal)  return ".";
        if (k == Keys.Divide)   return "/";
        if (k == Keys.Multiply) return "*";
        if (k == Keys.Subtract) return "-";
        if (k == Keys.Add)      return "+";
        return KeyNum(k);
    }

    // Lista de teclas desenhada no canto (pode desligar no .ini)
    private void DrawHelp()
    {
        string[] lines = {
            "~p~SERIAL KILLER RP",
            "~s~" + KeyNum(kBag)      + " Saco (pega/solta)",
            "~s~" + KeyNum(kBagTrunk) + " Saco -> porta-mala",
            "~s~" + KeyNum(kTie)      + " Amarrar NPC",
            "~s~" + KeyNum(kCarry)    + " Carregar corpo",
            "~s~" + KeyNum(kVicTrunk) + " Corpo -> porta-mala",
            "~s~" + KeyNum(kKnife)    + " Faca (matar)",
            "~s~" + KeyNum(kDrag)     + " Arrastar corpo",
            "~s~" + KeyNum(kDig)      + " Cavar/enterrar",
            "~s~" + KeyNum(kMask)     + " Mascara",
            "~s~" + KeyName(kClean)   + " Limpar vestigios",
            "~s~" + KeyNum(kPanic)    + " PANICO (reset)",
            "~s~" + KeyName(kHud)     + " Esconder este menu"
        };

        float y = 0.24f;
        for (int i = 0; i < lines.Length; i++)
        {
            DrawText(lines[i], 0.015f, y, 0.30f);
            y += 0.022f;
        }
    }

    // Barra de suspeita (sistema de evidencias) no canto superior direito
    private void DrawHeat()
    {
        float cx = 0.90f, cy = 0.175f, w = 0.10f, h = 0.016f;
        float frac = heat / 100f;

        // fundo
        Function.Call(Hash.DRAW_RECT, cx, cy, w + 0.006f, h + 0.008f, 0, 0, 0, 160);
        // preenchimento (verde -> amarelo -> vermelho), alinhado a esquerda
        int r = (int)(255f * Math.Min(1f, frac * 2f));
        int g = (int)(255f * Math.Min(1f, (1f - frac) * 2f));
        float fillX = cx - (w / 2f) + (w * frac / 2f);
        Function.Call(Hash.DRAW_RECT, fillX, cy, w * frac, h, r, g, 0, 220);

        DrawText("SUSPEITA " + (int)heat + "%", cx - 0.055f, cy - 0.028f, 0.28f);
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
