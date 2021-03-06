﻿using Castor.Emulator.Memory;
using Castor.Emulator.Utility;
using System.Collections.Generic;
using System.Text;

namespace Castor.Emulator.CPU
{
    public partial class Z80
    {
        #region References
        public ref byte A => ref _r.A;
        public ref byte F
        {
            get
            {
                _r.F &= 0xF0;
                return ref _r.F;
            }
        }
        public ref byte B => ref _r.B;
        public ref byte C => ref _r.C;
        public ref byte D => ref _r.D;
        public ref byte E => ref _r.E;
        public ref byte H => ref _r.H;
        public ref byte L => ref _r.L;

        public ref ushort AF
        {
            get
            {
                _r.F &= 0xF0;
                return ref _r.AF;
            }
        }

        public ref ushort BC => ref _r.BC;
        public ref ushort DE => ref _r.DE;
        public ref ushort HL => ref _r.HL;

        public ref ushort SP => ref _r.SP;
        public ref ushort PC => ref _r.PC;
        #endregion                

        #region Utility Functions
        private void InternalDelay(int cycles = 1) => _cycles += cycles * 4;

        private byte DecodeInstruction() { InternalDelay(); return _d.MMU[_r.Bump()]; }

        private byte ReadByte(int addr, int delay = 1)
        {
            InternalDelay(delay);
            return _d.MMU[addr];
        }

        private void WriteByte(int addr, byte value, int delay = 1)
        {
            InternalDelay(delay);
            _d.MMU[addr] = value;
        }

        private ushort ReadWord(int addr, int delay = 2)
        {
            InternalDelay(delay);
            return (ushort)(_d.MMU[addr + 1] << 8 | _d.MMU[addr]);
        }

        private void WriteWord(int addr, ushort value, int delay = 2)
        {
            InternalDelay(delay);
            _d.MMU[addr] = value.LSB();
            _d.MMU[addr + 1] = value.MSB();
        }

        private void PushWord(ushort value)
        {
            SP -= 2;
            WriteWord(SP, value);
        }

        private ushort PopWord()
        {
            var ret = ReadWord(SP);
            SP += 2;
            return ret;
        }

#if DEBUG
        private ushort Peek()
        {
            var ret = ReadWord(SP, 0);
            return ret;
        }
#endif

#if DEBUG
        private string ConstructString(List<byte> bytearray)
        {
            return Encoding.ASCII.GetString(bytearray.ToArray());
        }
#endif

        #endregion

        #region Internal Members
        private Registers _r;
        private Device _d;
        private InterruptMasterEnable _ime;

        private int _cycles;
        private bool _halted;
        #endregion;

        #region Constructor
        public Z80(Device d)
        {
            _d = d;
            _cycles = 0;
            _r = new Registers();
            _ime = InterruptMasterEnable.Disabled;
            _halted = false;
        }
        #endregion

        #region Step Methods
        public int Step()
        {
            _cycles = 0;

            if (!_halted)
            {
                var opcode = DecodeInstruction();
                Decode(opcode);
            }

            else
            {
                InternalDelay();
            }

            if (_d.IRQ.CanServiceInterrupts)
            {
                _halted = false;

                if (_ime == InterruptMasterEnable.Enabled)
                {
                    if (_d.IRQ.CanHandleInterrupt(InterruptFlags.VBL))
                    {
                        Restart(0x40);
                        _ime = InterruptMasterEnable.Disabled;
                        _d.IRQ.DisableInterrupt(InterruptFlags.VBL);
                    }

                    else if (_d.IRQ.CanHandleInterrupt(InterruptFlags.STAT))
                    {
                        Restart(0x48);
                        _ime = InterruptMasterEnable.Disabled;
                        _d.IRQ.DisableInterrupt(InterruptFlags.STAT);
                    }

                    else if (_d.IRQ.CanHandleInterrupt(InterruptFlags.Timer))
                    {
                        Restart(0x50);
                        _ime = InterruptMasterEnable.Disabled;
                        _d.IRQ.DisableInterrupt(InterruptFlags.Timer);
                    }

                    else if (_d.IRQ.CanHandleInterrupt(InterruptFlags.Serial))
                    {
                        Restart(0x58);
                        _ime = InterruptMasterEnable.Disabled;
                        _d.IRQ.DisableInterrupt(InterruptFlags.Serial);
                    }

                    else if (_d.IRQ.CanHandleInterrupt(InterruptFlags.Joypad))
                    {
                        Restart(0x60);
                        _ime = InterruptMasterEnable.Disabled;
                        _d.IRQ.DisableInterrupt(InterruptFlags.Joypad);
                    }
                }
            }

            if (_cycles >= 4 && _ime == InterruptMasterEnable.Enabling)
            {
                _ime = InterruptMasterEnable.Enabled;
            }

            return _cycles;
        }
        #endregion

        #region Instruction Implementations
        void Adc(int i)
        {
            A = AluAdd(this[R, i], true);
        }

        void Adc8()
        {
            A = AluAdd(N8, true);
        }

        void Add(int i)
        {
            A = AluAdd(this[R, i], false);
        }

        void Add8()
        {
            A = AluAdd(N8, false);
        }

        void AddHL(int i)
        {
            InternalDelay();
            HL = AluAddHL(this[RP, i]);
        }

        void AddSP()
        {
            InternalDelay(2);
            SP = AluAddSP(E8);
        }

        void And(int i)
        {
            A = AluAnd(this[R, i]);
        }

        void And8()
        {
            A = AluAnd(N8);
        }

        void Bit(int n, int i)
        {
            var operand = this[R, i];
            var result = (byte)((operand >> n) & 1);

            _r[Registers.Flags.Z] = result == 0;
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = true;
        }

        void Call()
        {
            InternalDelay();

            var address = N16;

            PushWord(PC);

            PC = address;
        }

        void Call(int i)
        {
            var address = N16;

            if (_r.CanJump(CC[i]))
            {
                InternalDelay();
                PushWord(PC);
                PC = address;
            }
        }

        void Ccf()
        {
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = !_r[Registers.Flags.C];
        }

        void Cp(int i)
        {
            AluSub(this[R, i], false);
        }

        void Cp8()
        {
            AluSub(N8, false);
        }

        void Cpl()
        {
            A = (byte)(~A);

            _r[Registers.Flags.N] = true;
            _r[Registers.Flags.H] = true;
        }

        void Daa()
        {
            int a = A;

            if (!_r[Registers.Flags.N])
            {
                if (_r[Registers.Flags.H] || (a & 0xF) > 0x09)
                    a += 0x06;

                if (_r[Registers.Flags.C] || a > 0x9F)
                    a += 0x60;
            }
            else
            {
                if (_r[Registers.Flags.H])
                    a = (a - 0x6) & 0xFF;

                if (_r[Registers.Flags.C])
                    a -= 0x60;
            }

            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.Z] = false;

            if ((a & 0x100) == 0x100)
                _r[Registers.Flags.C] = true;

            a &= 0xFF;

            if (a == 0)
                _r[Registers.Flags.Z] = true;

            A = (byte)a;
        }

        void Dec(int t, int i)
        {
            var operand = this[t, i];
            var result = (ushort)(operand - 1);

            this[t, i] = result;

            if (t == R)
            {
                _r[Registers.Flags.Z] = result == 0;
                _r[Registers.Flags.N] = true;
                _r[Registers.Flags.H] = result % 16 == 15;
            }
        }

        void Di()
        {
            _ime = InterruptMasterEnable.Disabled;
        }

        void Ei()
        {
            _ime = InterruptMasterEnable.Enabling;
        }

        void Halt()
        {
            _halted = true;
        }

        void Inc(int t, int i)
        {
            var v = this[t, i];
            var r = v + 1;

            this[t, i] = r;

            if (t == R)
            {
                _r[Registers.Flags.Z] = (byte)r == 0;
                _r[Registers.Flags.N] = false;
                _r[Registers.Flags.H] = (byte)r % 16 == 0;
            }
        }

        void JP()
        {
            ushort a = N16;

            InternalDelay();
            PC = a;
        }

        void JP(int i)
        {
            ushort a = N16;

            if (_r.CanJump(CC[i]))
            {
                InternalDelay();
                PC = a;
            }
        }

        void JR()
        {
            sbyte r = E8;

            InternalDelay();
            PC = (ushort)(PC + r);
        }

        void JR(int i)
        {
            sbyte r = E8;

            if (_r.CanJump(CC[i]))
            {
                InternalDelay();
                PC = (ushort)(PC + r);
            }
        }

        void JPHL()
        {
            PC = HL;
        }

        void Load(int t1, int i1, int t2, int i2)
        {
            this[t1, i1] = this[t2, i2];
        }

        void Load8(int i)
        {
            this[R, i] = N8;
        }

        void Load16(int i)
        {
            this[RP, i] = N16;
        }

        void LoadHL()
        {
            InternalDelay();

            HL = AluAddSP(E8);
        }

        void LoadSP()
        {
            InternalDelay();

            SP = HL;
        }

        void Or(int i)
        {
            A = AluOr(this[R, i]);
        }

        void Or8()
        {
            A = AluOr(N8);
        }

        void Pop(int i)
        {
            this[RP2, i] = PopWord();
        }

        void Push(int i)
        {
            InternalDelay();
            PushWord((ushort)this[RP2, i]);
        }

        void Res(int n, int i)
        {
            var operand = (byte)this[R, i];

            this[R, i] = Utility.Bit.ClearBit(operand, n);
        }

        void Ret()
        {
            InternalDelay();
            PC = PopWord();
        }

        void Ret(int i)
        {
            InternalDelay();

            if (_r.CanJump(CC[i]))
            {
                InternalDelay();
                PC = PopWord();
            }
        }

        void Reti()
        {
            _ime = InterruptMasterEnable.Enabled;
            Ret();
        }

        void Rl(int i)
        {
            this[R, i] = AluRl(this[R, i], true);
        }

        void Rla()
        {
            A = AluRl(A, true);
            _r[Registers.Flags.Z] = false;
        }

        void Rlc(int i)
        {
            this[R, i] = AluRl(this[R, i], false);
        }

        void Rlca()
        {
            A = AluRl(A, false);
            _r[Registers.Flags.Z] = false;
        }

        void Rr(int i)
        {
            this[R, i] = AluRr(this[R, i], true);
        }

        public void Rrc(int i)
        {
            this[R, i] = AluRr(this[R, i], false);
        }

        public void Rra()
        {
            A = AluRr(A, true);
            _r[Registers.Flags.Z] = false;
        }

        public void Rrca()
        {
            A = AluRr(A, false);
            _r[Registers.Flags.Z] = false;
        }

        public void Restart(int addr)
        {
            ushort result = (ushort)addr;

            InternalDelay();
            PushWord(PC);
            PC = result;
        }

        public void Sbc(int i)
        {
            A = AluSub(this[R, i], true);
        }

        public void Sbc8()
        {
            A = AluSub(N8, true);
        }

        public void Scf()
        {
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = true;
        }

        public void Set(int n, int i)
        {
            var operand = this[R, i];

            var result = Utility.Bit.SetBit((byte)operand, n);

            this[R, i] = result;
        }

        public void Sla(int i)
        {
            var operand = this[R, i];

            var shiftedBit = Utility.Bit.BitValue((byte)operand, 7);
            var result = (byte)(this[R, i] << 1);

            _r[Registers.Flags.Z] = result == 0;
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = shiftedBit == 1;

            this[R, i] = result;
        }

        public void Sra(int i)
        {
            var operand = this[R, i];

            var bit7 = Utility.Bit.BitValue((byte)operand, 7);
            var shiftedBit = Utility.Bit.BitValue((byte)operand, 0);
            var result = (byte)((this[R, i] >> 1) | (bit7 << 7));

            _r[Registers.Flags.Z] = result == 0;
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = shiftedBit == 1;

            this[R, i] = result;
        }

        public void Srl(int i)
        {
            var operand = this[R, i];

            var shiftedBit = Utility.Bit.BitValue((byte)operand, 0);
            var result = (byte)(this[R, i] >> 1);

            _r[Registers.Flags.Z] = result == 0;
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = shiftedBit == 1;

            this[R, i] = result;
        }

        public void Stop()
        {
            // _halted = true;
        }

        public void Sub(int i)
        {
            A = AluSub(this[R, i], false);
        }

        public void Sub8()
        {
            A = AluSub(N8, false);
        }

        public void Swap(int i)
        {
            var operand = this[R, i];

            var hi = (byte)(operand >> 4) & 0xF;
            var lo = (byte)(operand >> 0) & 0xF;

            var result = (byte)(lo << 4 | hi);

            _r[Registers.Flags.Z] = result == 0;
            _r[Registers.Flags.N] = false;
            _r[Registers.Flags.H] = false;
            _r[Registers.Flags.C] = false;

            this[R, i] = result;
        }

        public void Xor(int i)
        {
            A = AluXor(this[R, i]);
        }

        public void Xor8()
        {
            A = AluXor(N8);
        }

        public void Nop()
        {
        }


        #endregion
    }
}