﻿using System;
using System.Linq;
using UnityEngine;

using Database;
using FluentValidation;
using FluentValidation.Results;
using Nancy;
using System.Threading.Tasks;

namespace Web.Modules
{
    public class SimulationRequest
    {
        public string name;
        public int? map;
        public int[] vehicles;
        public bool? apiOnly;
        public bool? interactive;
        public bool? offScreen;
        public int? cluster;
        public DateTime? timeOfDay;
        public Weather weather;
    }

    public class SimulationResponse : WebResponse
    {
        public string Name;
        public string Status;
        public int? Map;
        public int[] Vehicles;
        public bool? ApiOnly;
        public bool? Interactive;
        public bool? OffScreen;
        public int? Cluster;
        public DateTime? TimeOfDay;
        public Weather Weather;
    }

    public class Weather
    {
        public float? rain;
        public float? fog;
        public float? wetness;
        public float? cloudiness;
    }

    public class SimulationModule : BaseModule<Simulation, SimulationRequest, SimulationResponse>
    {
        InlineValidator<Simulation> startValidator = new InlineValidator<Simulation>();
        public SimulationModule()
        {
            header = "simulations";
            Init();

            addValidator.RuleFor(o => (int)o.Map).Must(BeValidMap).WithMessage("You must specify a valid map id");

            editValidator.RuleFor(o => (int)o.Map).Must(BeValidMap).WithMessage("You must specify a valid map id");

            startValidator.RuleFor(o => (int)o.Map).Must(BeValidMap).WithMessage("You must specify a valid map id");
        }

        protected override void Init()
        {
            base.Init();
            Start();
            Stop();
        }

        protected void Start()
        {
            Post("/simulations/{id}/start", x =>
            {
                try
                {
                    Simulation model;
                    int id = x.id;
                    using (var db = DatabaseManager.Open())
                    {
                        model = db.Single<Simulation>(id);

                        ValidationResult startValidation = startValidator.Validate(model);
                        if (!startValidation.IsValid)
                        {
                            model.Status = "Invalid";
                            db.Update(model);

                            throw new Exception(startValidation.Errors.FirstOrDefault().ErrorMessage);
                        }
                        
                        Debug.Log($"Starting simulation {model.Name}");
                        model.Status = "Initializing";
                        NotificationManager.SendNotification(new ClientMessage("SimulationUpdate", ConvertToResponse(model)));

                        //foreach (string vehicleID in model.Vehicles.Split(','))
                        //{
                        //    BundleManager.instance.Load(new Uri(db.Single<Vehicle>(Convert.ToInt32(vehicleID)).Url).LocalPath);
                        //}

                        MainMenu.currentRunningId = model.Id;
                        db.Update(model);

                        BundleManager.instance.Load(new Uri(db.Single<Map>(model.Map).Url).LocalPath);
                        // TODO: initiate download boundObj here if needed
                        // ...
                    }

                    return model;
                }
                catch (IndexOutOfRangeException ex)
                {
                    Debug.Log($"Failed to start {typeof(Simulation).ToString()}: {ex.Message}.");
                    Response r = Response.AsJson(new
                    {
                        error = $"Failed to start {typeof(Simulation).ToString()}: {ex.Message}."
                    }, HttpStatusCode.NotFound);
                    return r;
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to start {typeof(Simulation).ToString()}: {ex.Message}.");
                    Response r = Response.AsJson(new
                    {
                        error = $"Failed to start {typeof(Simulation).ToString()}: {ex.Message}.",
                    }, HttpStatusCode.InternalServerError);
                    return r;
                }
            });
        }

        protected void Stop()
        {
            Post("/simulations/{id}/stop", x =>
            {
                try
                {
                    int id = x.id;
                    using (var db = DatabaseManager.Open())
                    {
                        var boundObj = db.Single<Simulation>(id);

                        if(boundObj.Status == "Running")
                        {
                            Deinit();
                        }

                        startValidator.ValidateAndThrow(boundObj);
                    }

                    return HttpStatusCode.OK;
                }
                catch (IndexOutOfRangeException ex)
                {
                    Debug.Log($"Failed to stop {typeof(Simulation).ToString()}: {ex.Message}.");
                    Response r = Response.AsJson(new
                    {
                        error = $"Failed to stop {typeof(Simulation).ToString()}: {ex.Message}."
                    }, HttpStatusCode.NotFound);
                    return r;
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to stop {typeof(Simulation).ToString()}: {ex.Message}.");
                    Response r = Response.AsJson(new
                    {
                        error = $"Failed to stop {typeof(Simulation).ToString()}: {ex.Message}.",
                    }, HttpStatusCode.InternalServerError);
                    return r;
                }
            });
        }

        protected async void Deinit()
        {
            using (var db = DatabaseManager.Open())
            {
                var boundObj = db.SingleOrDefault<Simulation>(MainMenu.currentRunningId);
                Debug.Log($"Deinitializing simulation {boundObj.Name}");
                boundObj.Status = "Deinitializing";
                db.Update(boundObj);
                NotificationManager.SendNotification(new ClientMessage("SimulationUpdate", SimulationModule.ConvertSimToResponse(boundObj)));

            }

            await Task.Delay(2000);

            using (var db = DatabaseManager.Open())
            {
                var boundObj = db.SingleOrDefault<Simulation>(MainMenu.currentRunningId);
                Debug.Log($"Stopping simulation {boundObj.Name}");
                boundObj.Status = "Idle";
                db.Update(boundObj);
                NotificationManager.SendNotification(new ClientMessage("SimulationUpdate", SimulationModule.ConvertSimToResponse(boundObj)));
                }
            }

        protected static bool BeValidMap(int mapId)
        {
            Map map = DatabaseManager.CurrentDb.SingleOrDefault<Map>(mapId);
            if(map == null)
            {
                Debug.Log($"Faild map validation: there is no map with id {mapId}");
            }

            return map != null;
        }

        protected static bool BeValidVehicles(string vehicleIds)
        {
            string[] ids = vehicleIds.Split(',');

            for (int i = 0; i < ids.Length; i++)
            {
                if (DatabaseManager.CurrentDb.SingleOrDefault<Vehicle>(Convert.ToInt32(ids[i])) == null)
                {
                    Debug.Log($"Failed vehicle validation: there is no vehicle with id {ids[i]}");
                    return false;
                }
            }

            return true;
        }

        protected override Simulation ConvertToModel(SimulationRequest simRequest)
        {
            Simulation simulation = new Simulation();
            simulation.Name = simRequest.name;
            simulation.Map = simRequest.map;
            simulation.ApiOnly = simRequest.apiOnly;
            simulation.Interactive = simRequest.interactive;
            simulation.OffScreen = simRequest.offScreen;
            simulation.Cluster = simRequest.cluster;
            simulation.TimeOfDay = simRequest.timeOfDay;

            if (simRequest.vehicles != null && simRequest.vehicles.Length > 0)
            {
                simulation.Vehicles = string.Join(",", simRequest.vehicles.Select(x => x.ToString()));
            }

            if (simRequest.weather != null)
            {
                simulation.Rain = simRequest.weather.rain;
                simulation.Fog = simRequest.weather.fog;
                simulation.Cloudiness = simRequest.weather.cloudiness;
                simulation.Wetness = simRequest.weather.wetness;
            }

            return simulation;
        }

        public static SimulationResponse ConvertSimToResponse(Simulation simulation)
        {
            SimulationResponse simResponse = new SimulationResponse();
            simResponse.Name = simulation.Name;
            simResponse.Status = simulation.Status;
            simResponse.Map = simulation.Map;
            simResponse.ApiOnly = simulation.ApiOnly;
            simResponse.Interactive = simulation.Interactive;
            simResponse.OffScreen = simulation.OffScreen;
            simResponse.Cluster = simulation.Cluster;
            simResponse.Id = simulation.Id;


            if (simulation.Vehicles != null && simulation.Vehicles.Length > 0)
            {
                simResponse.Vehicles = simulation.Vehicles.Split(',').Select(x => Convert.ToInt32(x)).ToArray();
            }

            simResponse.TimeOfDay = simulation.TimeOfDay;

            simResponse.Weather = new Weather()
            {
                rain = simulation.Rain,
                fog = simulation.Fog,
                wetness = simulation.Wetness,
                cloudiness = simulation.Cloudiness
            };

            return simResponse;
        }

        public override SimulationResponse ConvertToResponse(Simulation simulation)
        {
            SimulationResponse simResponse = new SimulationResponse();
            simResponse.Name = simulation.Name;
            simResponse.Status = simulation.Status;
            simResponse.Map = simulation.Map;
            simResponse.ApiOnly = simulation.ApiOnly;
            simResponse.Interactive = simulation.Interactive;
            simResponse.OffScreen = simulation.OffScreen;
            simResponse.Cluster = simulation.Cluster;
            simResponse.Id = simulation.Id;


            if (simulation.Vehicles != null && simulation.Vehicles.Length > 0)
            {
                simResponse.Vehicles = simulation.Vehicles.Split(',').Select(x => Convert.ToInt32(x)).ToArray();
            }

            simResponse.TimeOfDay = simulation.TimeOfDay;

            simResponse.Weather = new Weather()
            {
                rain = simulation.Rain,
                fog = simulation.Fog,
                wetness = simulation.Wetness,
                cloudiness = simulation.Cloudiness
            };

            return simResponse;
        }
    }
}