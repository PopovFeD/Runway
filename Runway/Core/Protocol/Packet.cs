namespace Runway.Protocol;

// Базовый тип для всех разобранных сообщений протокола. PacketParser.Parse
// возвращает Packet вместо прежнего object — вызывающий код матчит подтипы
// через switch-выражение и получает типизированные данные без кастов вслепую.
// Добавление нового типа сообщения = новый record + ветка в PacketParser
// (+ строка в MessageType и в mc_emulator.py — синхронизация пока ручная).
public abstract record Packet;

// Служебные сообщения без данных (Ping/Pong/Ack/Error) — их смысл целиком
// в самом MessageType, отдельные подтипы на каждый были бы шумом.
public sealed record ControlPacket(MessageType Type) : Packet;
