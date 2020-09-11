using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UIEngine;
using System.ComponentModel;
using System.Collections.ObjectModel;
using UIEngine.Core;

namespace CLITestProject
{
	public class DemographicModel
	{
		private const int _MAX_INIT_PEOPLE = 3;

		[Visible(nameof(Model))]
		public static DemographicModel Model { get; set; }

		[Visible(nameof(People))]
		public ObservableCollection<Person> People { get; } = new ObservableCollection<Person>();

		private readonly HashSet<Person> _Dead = new HashSet<Person>();

		private static Random _Random = new Random();
		public static bool _GetRandom(double prob)
		{
			double rnd = _Random.NextDouble();
			return rnd < prob;
		}

		public static void Init()
		{
			Model = new DemographicModel();
		}

		public DemographicModel()
		{
			for (int i = 0; i < _MAX_INIT_PEOPLE; i++)
			{
				var person = new Person(i % 2 == 0 ? Gender.Male : Gender.Female, null, null) 
				{ 
					Age = 20, 
					Prob_Die = 0.005, 
					Prob_Reproduce = 0.5 
				};
				People.Add(person.AppendVisibleAttribute(new VisibleAttribute("person")));
			}

			//var m = new Person(Gender.Male, null, null);
			//var f = new Person(Gender.Female, null, null);
			//m.Age = f.Age = 20;
			//m.Prob_Die = f.Prob_Die = 0.005;
			//m.Prob_Reproduce = f.Prob_Reproduce = 0.5;
			//m.Spouse = f;
			//f.Spouse = m;
			//m.Is_Married = f.Is_Married = true;
			//People.Add(m);
			//People.Add(f);

			Person.Died += me =>
			{
				//People.Remove(me);
				_Dead.Add(me);
				if (me.Spouse != null)
				{
					me.Spouse.Spouse = null;
				}
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
				me?.Siblings.ForEach(s => s.Siblings.Remove(me));
			};

			Person.FindForSpouse += me =>
			{
				foreach (var person in People)
				{
					if (person.IsWillingToMarry() && person != me && person.Gender != me.Gender)
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
				husband.Children.Add(child.AppendVisibleAttribute(new VisibleAttribute("person", "")));
				wife.Children.Add(child);
				child.Siblings = husband.Children.Where(c => !c.Equals(child)).ToList();
				child.Siblings.ForEach(s => s.Siblings.Add(child));
				People.Add(child);
			};
		}

		[Visible(nameof(TimeElapse))]
		public static void TimeElapse()
		{
			int tempCount;
			for (int i = 0; i < Model.People.Count; i++)
			{
				tempCount = Model.People.Count;
				Model.People[i].Grow();

				//i -= tempCount - Model.People.Count;
			}

			foreach (var person in Model._Dead)
			{
				Model.People.Remove(person);
			}
		}
	}

	public class Person : INotifyPropertyChanged, IVisible
	{
		public Person(Gender gender, Person father, Person mother)
		{
			Gender = gender;
			Father = father;
			Mother = mother;
		}

		public bool IsWillingToMarry() => DemographicModel._GetRandom(prob_Marry);

		private int _Age = 0;
		[Visible(nameof(Age))]
		public int Age
		{
			get => _Age;
			set
			{
				_Age = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Age), value);
			}
		}

		private Gender _Gender;
		[Visible(nameof(Gender))]
		public Gender Gender
		{
			get => _Gender;
			set
			{
				_Gender = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gender)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Gender), value);
			}
		}

		private bool _Is_Married = false;
		[Visible(nameof(Is_Married))]
		public bool Is_Married
		{
			get => _Is_Married;
			set
			{
				_Is_Married = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Is_Married)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Is_Married), value);
			}
		}

		[Visible(nameof(Children))]
		public List<Person> Children { get; } = new List<Person>();

		private Person _Father;
		[Visible(nameof(Father))]
		public Person Father
		{
			get => _Father;
			set
			{
				_Father = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Father)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Father), value);
			}
		}

		private Person _Mother;
		[Visible(nameof(Mother))]
		public Person Mother
		{
			get => _Mother;
			set
			{
				_Mother = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mother)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Mother), value);
			}
		}

		private Person _Spouse;
		[VisibleAttribute(nameof(Spouse))]
		public Person Spouse
		{
			get => _Spouse;
			set
			{
				_Spouse = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spouse)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Spouse), value);
			}
		}

		private List<Person> _Siblings = new List<Person>();
		[Visible(nameof(Siblings))]
		public List<Person> Siblings
		{
			get => _Siblings;
			set
			{
				_Siblings = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Siblings)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Siblings), value);
			}
		}

		private double _Prob_Die = 0.2;
		[Visible(nameof(Prob_Die))]
		public double Prob_Die
		{
			get => _Prob_Die;
			set
			{
				_Prob_Die = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prob_Die)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Die), value);
			}
		}

		private double prob_Marry = 0;
		[Visible(nameof(Prob_Marry))]
		public double Prob_Marry
		{
			get => prob_Marry;
			private set
			{
				if (value >= 0)
				{
					prob_Marry = value;
					//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Marry), value);
				}
				else
				{
					prob_Marry = 0;

					//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Marry), value);
				}
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prob_Marry)));
			}
		}


		private double _Prob_Reproduce = 0;
		[Visible(nameof(Prob_Reproduce))]
		public double Prob_Reproduce
		{
			get => _Prob_Reproduce;
			set
			{
				_Prob_Reproduce = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prob_Reproduce)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Reproduce), value);
			}
		}

		public string Name => "";

		public string Description => "";

		public string Header => "Someone";

		public static event Action<Person> Died;
		public static event Action<Person> FindForSpouse;
		public static event Action<Person, Person> Reproduce;
		public event PropertyChangedEventHandler PropertyChanged;

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

			if (Spouse != null && 
				Is_Married &&
				DemographicModel._GetRandom(
				Math.Sqrt(Prob_Reproduce * Spouse.Prob_Reproduce)))
			{
				if (Gender == Gender.Male)
				{
					Reproduce?.Invoke(this, Spouse);
				}
				else
				{
					Reproduce?.Invoke(Spouse, this);
				}
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
				Prob_Die = 5d / 1000;
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
			else if (Age < 25)
			{
				Prob_Marry += 10d / 100;
			}
			else if (Age < 40)
			{
				Prob_Marry += 5d / 100;
			}
			else if (Age < 50)
			{
				Prob_Marry += 1d / 100;
			}
			else
			{
				Prob_Marry -= 5d / 100;
			}
		}

		private void IncrementReproduceProb()
		{
			if (Age < 20)
			{

			}
			else if (Age == 20)
			{
				Prob_Reproduce = 50d / 100;
			}
			else if (Age < 30)
			{
				Prob_Reproduce -= 0.5d / 100;
			}
			else if (Age < 40)
			{
				Prob_Reproduce -= 1d / 100;
			}
			else if (Age < 50)
			{
				Prob_Reproduce -= 2d / 100;
			}
			else
			{
				Prob_Reproduce -= 1d / 10;
			}

			if (Prob_Reproduce <= 0)
			{
				Prob_Reproduce = 0;
			}
		}
	}
	public enum Gender
	{
		Male,
		Female
	}
}
