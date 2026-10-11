#pragma once

/*
 * Hook addresses for SB4E01 rev 0. Each value carries an annotation that
 * tools/verify_addresses.py checks against main.dol and the Syati symbol map:
 *   @sym NAME     value is the address of NAME
 *   @call NAME    instruction at value is `bl NAME`
 *   @branch NAME  instruction at value is `b NAME`
 * Vtable slots are patched with kmCondWritePointer, so the slot is only
 * overwritten if it still holds the expected original function.
 */

#define GCOOP_VT_SLOT_INIT     0x0C
#define GCOOP_VT_SLOT_MOVEMENT 0x14
#define GCOOP_VT_SLOT_KILL     0x34
#define GCOOP_VT_SLOT_OTHERMSG 0x6C

#define GCOOP_VT_MARIOACTOR             0x806C7448 /* @sym __vt__10MarioActor */
#define GCOOP_ORIG_MARIOACTOR_INIT      0x803B7820 /* @sym init__10MarioActorFRC12JMapInfoIter */
#define GCOOP_ORIG_MARIOACTOR_MOVEMENT  0x803C1800 /* @sym movement__10MarioActorFv */

/* Player character: MR::isPlayerLuigi tail-calls this; it reads bit 0 of GameDataPlayerStatus+0x10. */
#define GCOOP_FN_IS_LUIGI_PLAYER        0x804D3040 /* @sym isLuigiPlayer__16GameDataFunctionFv */
#define GCOOP_PLAYER_STATUS_LUIGI_BYTE  0x10

/*
 * Animation fields (no symbols, read off main.dol):
 *   MarioActor+0x588   MarioAnimator*        MarioAccess::getAnimationFrameMax, MarioAnimator::change
 *   MarioAnimator+0x4  XanimeResourceTable*  MarioAnimator::init stores the table it builds
 *   MarioAnimator+0x8  XanimePlayer* (body)  MarioAnimator::change
 *   XanimePlayer+0x10  f32 track weight[4]   changeTrackWeight: stfs f1, 0x10(this + track * 4)
 *   XanimePlayer+0x20  frame control         changeSpeed / MR::getBckCtrl: rate +0xC, frame +0x10, end (s16) +0x8
 *   XanimePlayer+0x60  current group         getCurrentAnimationName: name at +0
 *   sizeof(XanimePlayer) = 0x8C              0x80042354: li r3, 0x8C; bl __nw before the constructor
 *   LiveActor+0x54 ModelManager*, ModelManager+0x18 XanimePlayer*   ModelManager::startBck
 *   MarioAnimator+0xC  XanimePlayer* (upper body), +0x6C u8 upper active   changeUpper sets it to 1
 *   XanimePlayer+0x6C  XanimeCore*           constructor; MarioAnimator::init enables its joint transform
 *   XanimePlayer+0x88  u8[2] per-frame flags  ModelManager::calcAnim clears both; updateAfterMovement steps the frame only while clear
 * Mario's calcAnim layers the upper body over the body: upper->calcAnm(Spine1),
 * upper->overWriteMtxCalc(PartsControl), then clears both joints after the model is calculated.
 */
#define GCOOP_MARIOACTOR_ANIMATOR   0x588
#define GCOOP_ANIMATOR_TABLE        0x04
#define GCOOP_ANIMATOR_XANIME       0x08
#define GCOOP_ANIMATOR_UPPER        0x0C
#define GCOOP_ANIMATOR_UPPER_ACTIVE 0x6C
#define GCOOP_XANIME_TRACK_WEIGHT   0x10
#define GCOOP_XANIME_FRAME_CTRL     0x20
#define GCOOP_XANIME_GROUP          0x60
#define GCOOP_XANIME_CORE           0x6C
#define GCOOP_XANIME_CALCULATED     0x88
#define GCOOP_XANIME_SIZE           0x8C
#define GCOOP_MODELMANAGER_XANIME   0x18
#define GCOOP_FRAMECTRL_END         0x08
#define GCOOP_FRAMECTRL_RATE        0x0C
#define GCOOP_FRAMECTRL_FRAME       0x10

#define GCOOP_VT_GAMESCENE              0x806EFA28 /* @sym __vt__9GameScene */
#define GCOOP_ORIG_GAMESCENE_INIT       0x80451480 /* @sym init__9GameSceneFv */

#define GCOOP_VT_COIN                   0x8069E3C0 /* @sym __vt__4Coin */
#define GCOOP_ORIG_COIN_INIT            0x8028BD20 /* @sym init__4CoinFRC12JMapInfoIter */
#define GCOOP_CALL_COIN_REQUEST_GET     0x8028C620 /* @call requestGetCoin__4CoinFv */

#define GCOOP_VT_STARPIECE              0x806AECB0 /* @sym __vt__9StarPiece */
#define GCOOP_ORIG_STARPIECE_INIT       0x80311180 /* @sym init__9StarPieceFRC12JMapInfoIter */
#define GCOOP_CALL_STARPIECE_TOUCH_MSG  0x80314688 /* @call touchPlayer__9StarPieceFv */
#define GCOOP_CALL_STARPIECE_TOUCH_PTR  0x80312548 /* @call touchPlayer__9StarPieceFv */
#define GCOOP_CALL_STARPIECE_GROUP_RAIL 0x80318D00 /* @call initAndSetRailMoveFromGroup__9StarPieceFRC12JMapInfoIter */
#define GCOOP_CALL_STARPIECE_GROUP_FLOAT 0x80318D14 /* @call initAndSetFloatingFromGroup__9StarPieceFRC12JMapInfoIter */
#define GCOOP_MSG_AUTO_RUSH_BEGIN       147        /* isMsgAutoRushBegin__2MRFUl compares msg - 147 */

#define GCOOP_VT_POWERSTAR              0x806A86C8 /* @sym __vt__9PowerStar */
#define GCOOP_ORIG_POWERSTAR_INIT       0x802DF5C0 /* @sym init__9PowerStarFRC12JMapInfoIter */
#define GCOOP_ORIG_POWERSTAR_OTHERMSG   0x802DFFE0 /* @sym receiveOtherMsg__9PowerStarFUlP9HitSensorP9HitSensor */
#define GCOOP_CALL_POWERSTAR_GET_DEMO   0x802E0098 /* @call requestPowerStarGetDemo__2MRFv */
#define GCOOP_CALL_GRANDSTAR_GET_DEMO   0x802E0090 /* @call requestGrandStarGetDemo__2MRFv */

#define GCOOP_BR_SWITCH_A_ON            0x8026A394 /* @branch onSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */
#define GCOOP_BR_SWITCH_B_ON            0x8026A3D4 /* @branch onSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */
#define GCOOP_BR_SWITCH_DEAD_ON         0x8026A434 /* @branch onSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */
#define GCOOP_BR_SWITCH_A_OFF           0x8026A3A4 /* @branch offSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */
#define GCOOP_BR_SWITCH_B_OFF           0x8026A3E4 /* @branch offSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */
#define GCOOP_BR_SWITCH_DEAD_OFF        0x8026A444 /* @branch offSwitchBySwitchIdInfo__19StageSwitchFunctionFRC12SwitchIdInfo */

/* Comet stars. These MR functions are a single branch, so the hook calls the real EventFunction. */
#define GCOOP_BR_COMET_ON               0x80024F20 /* @branch startCometEvent__13EventFunctionFv */
#define GCOOP_BR_COMET_OFF              0x80024F30 /* @branch endCometEvent__13EventFunctionFv */

/* NameObjRegister::add loads the holder and tail-calls NameObjHolder::add. The hook sees (holder, object). */
#define GCOOP_BR_NAMEOBJ_ADD            0x80343E34 /* @branch add__13NameObjHolderFP7NameObj */

/* Enemies whose defeat is shared (opt-in, GCOOP_CF_SYNC_ENEMY): vtable, init, kill */
#define GCOOP_VT_ENEMY_KURIBO         0x8068B7C8 /* @sym __vt__6Kuribo */
#define GCOOP_ORIG_KURIBO_INIT   0x801B8A40 /* @sym init__6KuriboFRC12JMapInfoIter */
#define GCOOP_ORIG_KURIBO_KILL   0x801B8E80 /* @sym kill__6KuriboFv */
#define GCOOP_VT_ENEMY_KURIBOMINI     0x8068BCA8 /* @sym __vt__10KuriboMini */
#define GCOOP_ORIG_KURIBOMINI_INIT 0x801BCCE0 /* @sym init__10KuriboMiniFRC12JMapInfoIter */
#define GCOOP_ORIG_KURIBOMINI_KILL 0x801BCF90 /* @sym kill__10KuriboMiniFv */
#define GCOOP_VT_ENEMY_TERESA         0x806929C8 /* @sym __vt__6Teresa */
#define GCOOP_ORIG_TERESA_INIT   0x8020BEB0 /* @sym init__6TeresaFRC12JMapInfoIter */
#define GCOOP_ORIG_TERESA_KILL   0x8020C340 /* @sym kill__6TeresaFv */
#define GCOOP_VT_ENEMY_TAKOBO         0x80691D60 /* @sym __vt__6Takobo */
#define GCOOP_ORIG_TAKOBO_INIT   0x802039A0 /* @sym init__6TakoboFRC12JMapInfoIter */
#define GCOOP_ORIG_TAKOBO_KILL   0x80203DF0 /* @sym kill__6TakoboFv */
#define GCOOP_VT_ENEMY_MERAMERA       0x8068CBE0 /* @sym __vt__8Meramera */
#define GCOOP_ORIG_MERAMERA_INIT 0x801C6520 /* @sym init__8MerameraFRC12JMapInfoIter */
#define GCOOP_ORIG_MERAMERA_KILL 0x801C67E0 /* @sym kill__8MerameraFv */
#define GCOOP_VT_ENEMY_BEGOMANSPIKE   0x80683F68 /* @sym __vt__12BegomanSpike */
#define GCOOP_ORIG_BEGOMANSPIKE_INIT 0x80163570 /* @sym init__12BegomanSpikeFRC12JMapInfoIter */
#define GCOOP_ORIG_BEGOMANSPIKE_KILL 0x8015F9E0 /* @sym kill__11BegomanBaseFv */
#define GCOOP_VT_ENEMY_BEGOMANBABY    0x8068365C /* @sym __vt__11BegomanBaby */
#define GCOOP_ORIG_BEGOMANBABY_INIT 0x8015D2E0 /* @sym init__11BegomanBabyFRC12JMapInfoIter */
#define GCOOP_ORIG_BEGOMANBABY_KILL 0x8015D5F0 /* @sym kill__11BegomanBabyFv */
#define GCOOP_VT_ENEMY_GESSO          0x80686E48 /* @sym __vt__5Gesso */
#define GCOOP_ORIG_GESSO_INIT    0x80182900 /* @sym init__5GessoFRC12JMapInfoIter */
#define GCOOP_ORIG_GESSO_KILL    0x80182B30 /* @sym kill__5GessoFv */
#define GCOOP_VT_ENEMY_PUKUPUKU       0x806901C0 /* @sym __vt__8Pukupuku */
#define GCOOP_ORIG_PUKUPUKU_INIT 0x801F05B0 /* @sym init__8PukupukuFRC12JMapInfoIter */
#define GCOOP_ORIG_PUKUPUKU_KILL 0x801F06E0 /* @sym kill__8PukupukuFv */
#define GCOOP_VT_ENEMY_UNIZO          0x806937E8 /* @sym __vt__5Unizo */
#define GCOOP_ORIG_UNIZO_INIT    0x80216E10 /* @sym init__5UnizoFRC12JMapInfoIter */
#define GCOOP_ORIG_UNIZO_KILL    0x802174A0 /* @sym kill__5UnizoFv */
#define GCOOP_VT_ENEMY_MOGUCCHI       0x8068D1C0 /* @sym __vt__8Mogucchi */
#define GCOOP_ORIG_MOGUCCHI_INIT 0x801CC3B0 /* @sym init__8MogucchiFRC12JMapInfoIter */
#define GCOOP_ORIG_MOGUCCHI_KILL 0x801CC660 /* @sym kill__8MogucchiFv */
#define GCOOP_VT_ENEMY_KAMECK         0x80689D30 /* @sym __vt__6Kameck */
#define GCOOP_ORIG_KAMECK_INIT   0x801A4990 /* @sym init__6KameckFRC12JMapInfoIter */
#define GCOOP_ORIG_KAMECK_KILL   0x801A4DC0 /* @sym kill__6KameckFv */
#define GCOOP_VT_ENEMY_KOTEBUG        0x8068B530 /* @sym __vt__7KoteBug */
#define GCOOP_ORIG_KOTEBUG_INIT  0x801B6230 /* @sym init__7KoteBugFRC12JMapInfoIter */
#define GCOOP_ORIG_KOTEBUG_KILL  0x801B64B0 /* @sym kill__7KoteBugFv */
#define GCOOP_VT_ENEMY_KANINA         0x8068A6A0 /* @sym __vt__6Kanina */
#define GCOOP_ORIG_KANINA_INIT   0x801A97F0 /* @sym init__6KaninaFRC12JMapInfoIter */
#define GCOOP_ORIG_KANINA_KILL   0x801A9A20 /* @sym kill__6KaninaFv */
#define GCOOP_VT_ENEMY_PACKUNPETIT    0x8068EFD8 /* @sym __vt__11PackunPetit */
#define GCOOP_ORIG_PACKUNPETIT_INIT 0x801E2680 /* @sym init__11PackunPetitFRC12JMapInfoIter */
#define GCOOP_ORIG_PACKUNPETIT_KILL 0x801E3390 /* @sym kill__11PackunPetitFv */
#define GCOOP_VT_ENEMY_CHOROBON       0x80685C60 /* @sym __vt__8Chorobon */
#define GCOOP_ORIG_CHOROBON_INIT 0x80176AA0 /* @sym init__8ChorobonFRC12JMapInfoIter */
#define GCOOP_ORIG_CHOROBON_KILL 0x80237500 /* @sym kill__9LiveActorFv */
#define GCOOP_VT_ENEMY_KARIKARI       0x8068AB10 /* @sym __vt__8Karikari */
#define GCOOP_ORIG_KARIKARI_INIT 0x801ADFF0 /* @sym init__8KarikariFRC12JMapInfoIter */
#define GCOOP_ORIG_KARIKARI_KILL 0x801AFC10 /* @sym kill__8KarikariFv */
#define GCOOP_VT_ENEMY_TAKOHEI        0x80692130 /* @sym __vt__7TakoHei */
#define GCOOP_ORIG_TAKOHEI_INIT  0x80205C60 /* @sym init__7TakoHeiFRC12JMapInfoIter */
#define GCOOP_ORIG_TAKOHEI_KILL  0x80205DE0 /* @sym kill__7TakoHeiFv */
#define GCOOP_VT_ENEMY_TOGEZO         0x80692FE8 /* @sym __vt__6Togezo */
#define GCOOP_ORIG_TOGEZO_INIT   0x80211190 /* @sym init__6TogezoFRC12JMapInfoIter */
#define GCOOP_ORIG_TOGEZO_KILL   0x802113E0 /* @sym kill__6TogezoFv */

#define GCOOP_ENEMY_TABLE(X) \
    X(KURIBO) \
    X(KURIBOMINI) \
    X(TERESA) \
    X(TAKOBO) \
    X(MERAMERA) \
    X(BEGOMANSPIKE) \
    X(BEGOMANBABY) \
    X(GESSO) \
    X(PUKUPUKU) \
    X(UNIZO) \
    X(MOGUCCHI) \
    X(KAMECK) \
    X(KOTEBUG) \
    X(KANINA) \
    X(PACKUNPETIT) \
    X(CHOROBON) \
    X(KARIKARI) \
    X(TAKOHEI) \
    X(TOGEZO)
