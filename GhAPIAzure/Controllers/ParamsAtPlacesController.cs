﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using GhAPIAzure.Models;
using Swashbuckle.Swagger.Annotations;
using DatabasePOCOs.Global;

namespace GhAPIAzure.Controllers
{
    [AllowAnonymous]
    public class ParamsAtPlacesController : ApiController
    {
        private Models.DbContext db = new Models.DbContext();

        // GET: api/ParamsAtPlaces
        [SwaggerOperation("GetAll")]
        public IQueryable<ParamAtPlace> GetParamsAtPlaces()
        {
            return db.ParamsAtPlaces;
        }
    }
}