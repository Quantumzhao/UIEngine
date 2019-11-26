using System;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Demo
{
	public class DemographicModel
	{
		private static Random _Random = new Random();
		public static bool _GetRandom(double prob)
		{
			double rnd = _Random.NextDouble();
			return rnd < prob;
		}

		public DemographicModel()
		{
			People.Add(new Person(Gender.Male, null, null));
			People.Add(new Person(Gender.Female, null, null));

			Person.Died += me =>
			{
				People.Remove(me);
				me.Spouse.Spouse = null;
				me.Children.ForEach(c => {
					if (me.Gender == Gender.Male)
					{
						c.Father = null;
					}
					else
					{
						c.Mother = null;
					}
				});
				me.Siblings.ForEach(s => s.Siblings.Remove(me));
			};

			Person.FindForSpouse += me =>
			{
				foreach (var person in People)
				{
					if (person.IsWillingToMarry())
					{
						me.Is_Married = true;
						person.Is_Married = true;
						me.Spouse = person;
						person.Spouse = me;
						break;
					}
				}
			};

			Person.Reproduce += (husband, wife) =>
			{
				Person child;
				if (_GetRandom(0.5))
				{
					child = new Person(Gender.Male, husband, wife);
				}
				else
				{
					child = new Person(Gender.Female, husband, wife);
				}
				husband.Children.Add(child);
				wife.Children.Add(child);
				child.Siblings = husband.Children.Where(c => !c.Equals(child)).ToList();
				child.Siblings.ForEach(s => s.Siblings.Add(child));
				People.Add(child);
			};
		}

		private Timer Timer = new Timer(1000);
		public List<Person> People = new List<Person>();

		public void StartSimulation()
		{
			Timer.Elapsed += (sender, e) =>
			{
				People.ForEach(p => p.Grow());
			};
			Timer.Start();
		}
	}

	public class Person
	{
		public Person(Gender gender, Person father, Person mother)
		{
			Gender = gender;
			Father = father;
			Mother = mother;
		}


		public bool IsWillingToMarry() => DemographicModel._GetRandom(prob_Marry);

		public int Age { get; set; } = 0;
		public Gender Gender { get; set; }
		public bool Is_Married { get; set; } = false;
		public List<Person> Children { get; set; } = new List<Person>();
		public Person Father { get; set; }
		public Person Mother { get; set; }
		public Person Spouse { get; set; }
		public List<Person> Siblings { get; set; } = new List<Person>();
		public double Prob_Die { get; private set; } = 0.2;

		private double prob_Marry = 0;
		public double Prob_Marry 
		{ 
			get => prob_Marry; 
			private set
			{
				if (value >= 0)
				{
					prob_Marry = value;
				}
				else
				{
					prob_Marry = 0;
				}
			}
		}
		public double Prob_Reproduce { get; private set; } = 0;

		public static event Action<Person> Died;
		public static event Action<Person> FindForSpouse;
		public static event Action<Person, Person> Reproduce;

		public void Grow()
		{
			Age++;
			IncrementMarriageProb();
			IncrementReproduceProb();
			IncrementDeathProb();

			if (DemographicModel._GetRandom(Prob_Die))
			{
				Died?.Invoke(this);
			}

			if (DemographicModel._GetRandom(Prob_Marry))
			{
				FindForSpouse?.Invoke(this);
			}

			if (DemographicModel._GetRandom(Math.Sqrt(Prob_Reproduce * Spouse.Prob_Reproduce)))
			{
				Reproduce?.Invoke(this, Spouse);
			}
		}

		private void IncrementDeathProb()
		{
			if (Age < 3)
			{
				Prob_Die -= 1d / 30;
				// end with 0.1
			}
			else if (Age < 12)
			{
				Prob_Die -= 1d / 100;
				// end with 0.01
			}
			else if (Age < 20)
			{
				Prob_Die = 5d / 10000;
			}
			else if (Age < 35)
			{
				Prob_Die += 1d / 1000;
			}
			else if (Age < 60)
			{
				Prob_Die += 5d / 1000;
			}
			else if (Age < 80)
			{
				Prob_Die += 1d / 100;
			}
			else
			{
				Prob_Die += 1d / 50;
			}
		}

		private void IncrementMarriageProb()
		{
			if (Is_Married)
			{
				prob_Marry = 0;
				return;
			}

			if (Age < 20)
			{
				Prob_Marry = 0;
			}
			else if (Age < 35)
			{
				Prob_Marry += 4d / 100;
			}
			else if (Age < 40)
			{
				Prob_Marry += 3d / 100;
			}
			else if (Age < 50)
			{
				Prob_Marry += 1d / 200;
			}
			else
			{
				Prob_Marry -= 5d / 100;
			}
		}

		private void IncrementReproduceProb()
		{
			if (!Is_Married || Spouse == null)
			{
				Prob_Reproduce = 0;
				return;
			}

			if (Age == 20)
			{
				Prob_Reproduce = 1d / 10;
			}
			else if (Age < 35)
			{
				Prob_Reproduce += 1d / 100;
			}
			else if (Age < 45)
			{
				Prob_Reproduce += 1d / 200;
			}
			else if (Age < 50)
			{
				Prob_Reproduce -= 1d / 100;
			}
			else
			{
				Prob_Reproduce -= 1d / 10;
			}

			if (Children.Count <= 2)
			{
				Prob_Reproduce *= 1d / 2;
			}
			else if (Children.Count <= 4)
			{
				Prob_Reproduce *= 1d / 8;
			}
			else
			{
				Prob_Reproduce *= 1d / 20;
			}
		}
	}
	public enum Gender
	{
		Male,
		Female
	}
}
