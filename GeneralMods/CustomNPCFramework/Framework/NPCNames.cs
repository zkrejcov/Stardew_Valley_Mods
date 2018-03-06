﻿using CustomNPCFramework.Framework.Enums;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomNPCFramework.Framework
{
    public class NPCNames
    {
        public static List<string> maleNames = new List<string>()
        {
            "Freddy",
            "Josh",
            "Ash"
        };

        public static List<string> femaleNames = new List<string>()
        {
            "Rebecca",
            "Sierra",
            "Lisa"
        };

        public static List<string> otherGenderNames = new List<string>()
        {
            "Jayden",
            "Ryanne",
            "Skylar"
        };

        public static string getRandomNPCName(Genders gender)
        {
            if (gender == Genders.female) {

                int rand = Game1.random.Next(0, femaleNames.Count - 1);
                return femaleNames.ElementAt(rand);
            }

            if (gender == Genders.male)
            {

                int rand = Game1.random.Next(0, maleNames.Count - 1);
                return maleNames.ElementAt(rand);
            }

            if (gender == Genders.other)
            {

                int rand = Game1.random.Next(0, otherGenderNames.Count - 1);
                return otherGenderNames.ElementAt(rand);
            }

            return "";

        }
    }
}