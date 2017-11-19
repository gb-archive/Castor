﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Castor.Emulator.Cartridge
{
    public static class CartridgeFactory
    {
        public static ICartridge CreateCartridge(byte[] bytecode, GameboySystem system)
        {
            byte romTypeSelect = bytecode[0x147];

            switch (romTypeSelect)
            {
                case 0x00:
                    return new MBC0(bytecode, system);
                default:
                    throw new Exception("Unsupported MBC Type was found!");
            }
        }
    }
}
