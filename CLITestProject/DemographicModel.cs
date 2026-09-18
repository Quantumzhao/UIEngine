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

		private static readonly Random _RANDOM = new Random();
		public static bool GetRandom(double prob)
		{
			double rnd = _RANDOM.NextDouble();
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
				var person = new Person(i % 2 == 0 ? Gender.MALE : Gender.FEMALE, null, null)
				{ 
					Age = 20, 
					Prob_Die = 0.005, 
					Prob_Reproduce = 0.5 
				};
				People.Add(person.AppendVisibleAttribute(new VisibleAttribute("person")));
			}

			Person.Died += me =>
			{
				_Dead.Add(me);
				if (me.Spouse != null)
				{
					me.Spouse.Spouse = null;
				}
				me.Children.ForEach(c => {
					if (me.Gender == Gender.MALE)
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
				if (GetRandom(0.5))
				{
					child = new Person(Gender.MALE, husband, wife);
				}
				else
				{
					child = new Person(Gender.FEMALE, husband, wife);
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

		public bool IsWillingToMarry() => DemographicModel.GetRandom(_ProbMarry);

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

		private bool _IsMarried = false;
		[Visible(nameof(Is_Married))]
		public bool Is_Married
		{
			get => _IsMarried;
			set
			{
				_IsMarried = value;

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
		[Visible(nameof(Spouse))]
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

		private double _ProbDie = 0.2;
		[Visible(nameof(Prob_Die))]
		public double Prob_Die
		{
			get => _ProbDie;
			set
			{
				_ProbDie = value;

				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prob_Die)));
				//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Die), value);
			}
		}

		private double _ProbMarry = 0;
		[Visible(nameof(Prob_Marry))]
		public double Prob_Marry
		{
			get => _ProbMarry;
			private set
			{
				if (value >= 0)
				{
					_ProbMarry = value;
					//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Marry), value);
				}
				else
				{
					_ProbMarry = 0;

					//Dashboard.NotifyPropertyChanged(this, nameof(Prob_Marry), value);
				}
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Prob_Marry)));
			}
		}


		private double _ProbReproduce = 0;
		[Visible(nameof(Prob_Reproduce))]
		public double Prob_Reproduce
		{
			get => _ProbReproduce;
			set
			{
				_ProbReproduce = value;

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
			_IncrementMarriageProb();
			_IncrementReproduceProb();
			_IncrementDeathProb();

			if (DemographicModel.GetRandom(Prob_Die))
			{
				Died?.Invoke(this);
			}

			if (DemographicModel.GetRandom(Prob_Marry))
			{
				FindForSpouse?.Invoke(this);
			}

			if (Spouse != null && 
				Is_Married &&
				DemographicModel.GetRandom(
				Math.Sqrt(Prob_Reproduce * Spouse.Prob_Reproduce)))
			{
				if (Gender == Gender.MALE)
				{
					Reproduce?.Invoke(this, Spouse);
				}
				else
				{
					Reproduce?.Invoke(Spouse, this);
				}
			}
		}

		private void _IncrementDeathProb()
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

		private void _IncrementMarriageProb()
		{
			if (Is_Married)
			{
				_ProbMarry = 0;
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

		private void _IncrementReproduceProb()
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
		MALE,
		FEMALE
	}
}
